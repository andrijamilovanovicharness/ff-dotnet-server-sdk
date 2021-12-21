using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using io.harness.cfsdk.client.api.rules;
using io.harness.cfsdk.client.dto;
using io.harness.cfsdk.HarnessOpenAPIService;
using Newtonsoft.Json.Linq;

namespace io.harness.cfsdk.client.api
{
    interface IEvaluatorCallback
    {
        void evaluationProcessed(FeatureConfig featureConfig, dto.Target target, Variation variation);
    }
    interface IEvaluator
    {
        bool BoolVariation(string key, dto.Target target, bool defaultValue);
        string StringVariation(string key, dto.Target target, string defaultValue);
        double NumberVariation(string key, dto.Target target, double defaultValue);
        JObject JsonVariation(string key, dto.Target target, JObject defaultValue);
    }
    internal class Evaluator : IEvaluator
    {
        private IRepository repository;
        private IEvaluatorCallback callback;
        public Evaluator(IRepository repository, IEvaluatorCallback callback)
        {
            this.repository = repository;
            this.callback = callback;
        }
        private Variation EvaluateVariation(string key, dto.Target target, FeatureConfigKind kind)
        {
            FeatureConfig featureConfig = this.repository.GetFlag(key);
            if (featureConfig == null || featureConfig.Kind != kind)
                return null;

            ICollection<Prerequisite> prerequisites = featureConfig.Prerequisites;
            if (prerequisites != null)
            {
                bool prereq = checkPreRequisite(featureConfig, target);
                if( !prereq)
                {
                    return featureConfig.Variations.First(v => v.Identifier.Equals(featureConfig.OffVariation));
                }
            }

            Variation var = Evaluate(featureConfig, target);
            if(var != null)
            {
                this.callback.evaluationProcessed(featureConfig, target, var);
            }
            return var;
        }

        public bool BoolVariation(string key, dto.Target target, bool defaultValue)
        {
            Variation variation = EvaluateVariation(key, target, FeatureConfigKind.Boolean);
            bool res;
            return (variation != null && Boolean.TryParse(variation.Value, out res)) ? res : defaultValue;
        }

        public JObject JsonVariation(string key, dto.Target target, JObject defaultValue)
        {
            Variation variation = EvaluateVariation(key, target, FeatureConfigKind.Json);
            return variation != null ? JObject.Parse(variation.Value) : defaultValue;
        }

        public double NumberVariation(string key, dto.Target target, double defaultValue)
        {
            Variation variation = EvaluateVariation(key, target, FeatureConfigKind.Int);
            double res;
            return (variation != null && Double.TryParse(variation.Value, out res)) ? res : defaultValue;
        }

        public string StringVariation(string key, dto.Target target, string defaultValue)
        {
            Variation variation = EvaluateVariation(key, target, FeatureConfigKind.String);
            return variation != null ? variation.Value : defaultValue;
        }

        private bool checkPreRequisite(FeatureConfig parentFeatureConfig, dto.Target target)
        {
            bool result = true;
            List<Prerequisite> prerequisites = parentFeatureConfig.Prerequisites.ToList();
            if ( prerequisites != null && prerequisites.Count > 0)
            {
                foreach (Prerequisite pqs in prerequisites)
                {
                    FeatureConfig preReqFeatureConfig = this.repository.GetFlag(pqs.Feature);
                    if (preReqFeatureConfig == null)
                    {
                        return true;
                    }

                    // Pre requisite variation value evaluated below
                    Variation preReqEvaluatedVariation = Evaluate(preReqFeatureConfig, target);
                    if(preReqEvaluatedVariation == null)
                    {
                        return true;
                    }

                    List<string> validPreReqVariations = pqs.Variations.ToList();
                    if (!validPreReqVariations.Contains(preReqEvaluatedVariation.ToString()))
                    {
                        return false;
                    }
                    else
                    {
                        result = checkPreRequisite(preReqFeatureConfig, target);
                    }
                }
            }
            return result;
        }
        private Variation Evaluate(FeatureConfig featureConfig, dto.Target target)
        {
            string variation = featureConfig.OffVariation;
            if (featureConfig.State == FeatureState.On)
            {
                variation = null;
                if (featureConfig.VariationToTargetMap != null)
                {
                    variation = evaluateVariationMap(target, featureConfig.VariationToTargetMap);
                }
                if (variation == null)
                {
                    variation = evaluateRules(featureConfig, target);
                }
                if (variation == null)
                {
                    variation = evaluateDistribution(featureConfig, target);
                }
                if (variation == null)
                {
                    variation = featureConfig.DefaultServe.Variation;
                }
            }

            if(variation != null && featureConfig.Variations != null)
            {
                return featureConfig.Variations.First(var => var.Identifier.Equals(variation));
            }
            return null;

        }

        private string evaluateVariationMap(dto.Target target, ICollection<VariationMap> variationMaps)
        {
            if (variationMaps == null)
            {
                return null;
            }
            foreach (VariationMap variationMap in variationMaps)
            {
                ICollection<TargetMap> targets = variationMap.Targets;

                if (targets != null)
                {
                    foreach (TargetMap targetMap in targets)
                    {
                        if (targetMap.Identifier.Contains(target.Identifier))
                        {
                            return variationMap.Variation;
                        }
                    }
                }

                ICollection<string> segmentIdentifiers = variationMap.TargetSegments;
                if (segmentIdentifiers != null)
                {
                    foreach (string segmentIdentifier in segmentIdentifiers)
                    {
                        Segment segment = this.repository.GetSegment(segmentIdentifier);
                        if (segment != null)
                        {
                            ICollection<HarnessOpenAPIService.Target> includedTargets = segment.Included;
                            if (includedTargets != null)
                            {
                                foreach (HarnessOpenAPIService.Target includedTarget in includedTargets)
                                {
                                    if (includedTarget.Identifier.Contains(target.Identifier))
                                    {
                                        return variationMap.Variation;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        private string evaluateRules(FeatureConfig featureConfig, dto.Target target)
        {
            ICollection<ServingRule> originalServingRules = featureConfig.Rules;
            List<ServingRule> servingRules = ((List<ServingRule>)originalServingRules).OrderBy(sr => sr.Priority).ToList();


            string servedVariation = null;
            foreach (ServingRule servingRule in servingRules)
            {
                servedVariation = evaluateServingRule(servingRule, target);
                if (servedVariation != null)
                {
                    return servedVariation;
                }
            }
            return null;
        }

        private string evaluateServingRule(ServingRule servingRule, dto.Target target)
        {
            foreach (Clause clause in servingRule.Clauses.Where(cl => cl != null))
            {
                if (!process(clause, target))
                { // check if the target match the clause
                    return null;
                }
            }

            Serve serve = servingRule.Serve;
            string servedVariation;
            if (serve.Variation != null)
            {
                servedVariation = serve.Variation;
            }
            else
            {
                DistributionProcessor distributionProcessor = new DistributionProcessor(servingRule.Serve);
                servedVariation = distributionProcessor.loadKeyName(target);
            }
            return servedVariation;
        }

        private string evaluateDistribution(FeatureConfig featureConfig, dto.Target target)
        {
            DistributionProcessor distributionProcessor = new DistributionProcessor(featureConfig.DefaultServe);
            return distributionProcessor.loadKeyName(target);
        }

        private bool process(Clause clause, dto.Target target)
        {
            bool result = compare(clause.Values.ToList(), target, clause);
            return result;
        }

        private bool compare(List<string> value, dto.Target target, Clause clause)
        {
            string Operator = clause.Op;
            string Object = null;
            object attrValue = null;
            try
            {
                attrValue = getAttrValue(target, clause.Attribute);
            }
            catch (CfClientException e)
            {
                attrValue = "";
            }
            Object = attrValue.ToString();

            if (clause.Values == null)
            {
                throw new CfClientException("The clause is missing values");
            }

            string v = value[0];
            switch (Operator)
            {
                case "starts_with":
                    return Object.StartsWith(v);
                case "ends_with":
                    return Object.EndsWith(v);
                case "match":
                    Regex rgx = new Regex(v);
                    return rgx.IsMatch(Object);
                case "contains":
                    return Object.Contains(v);
                case "equal":
                    return Object.ToLower().Equals(v.ToLower());
                case "equal_sensitive":
                    return Object.Equals(v);
                case "in":
                    return value.Contains(Object);
                case "segmentMatch":
                    foreach (string segmentIdentifier in value)
                    {
                        Segment segment = this.repository.GetSegment(segmentIdentifier);
                        if (segment != null)
                        {
                            List<HarnessOpenAPIService.Target> excludedTargets = segment.Excluded.ToList();
                            if (excludedTargets != null)
                            {
                                foreach (HarnessOpenAPIService.Target excludeTarget in excludedTargets)
                                {
                                    if (excludeTarget.Identifier.Contains(target.Identifier))
                                    {
                                        return false;
                                    }
                                }
                            }
                            List<HarnessOpenAPIService.Target> includedTargets = segment.Included.ToList();
                            if (includedTargets != null)
                            {
                                foreach (HarnessOpenAPIService.Target includedTarget in includedTargets)
                                {
                                    if (includedTarget.Identifier.Contains(target.Identifier))
                                    {
                                        return true;
                                    }
                                }
                            }
                            if (segment.Rules != null)
                            {
                                foreach (Clause rule in segment.Rules)
                                {
                                    if (compare(rule.Values.ToList(), target, rule) == true)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }

        public static object getAttrValue(dto.Target target, string attribute)
        {
            switch (attribute)
            {
                case "identifier":
                    return target.Identifier;
                case "name":
                    return target.Name;
                default:
                    if (target.Attributes != null & target.Attributes.ContainsKey(attribute))
                    {
                        return target.Attributes[attribute];
                    }
                    throw new CfClientException("The attribute" + attribute + " does not exist");
            }

        }
    }
}
