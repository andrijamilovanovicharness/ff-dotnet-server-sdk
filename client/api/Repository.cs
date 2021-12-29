﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using io.harness.cfsdk.client.cache;
using io.harness.cfsdk.HarnessOpenAPIService;

[assembly: InternalsVisibleToAttribute("ff-server-sdk-test")]

namespace io.harness.cfsdk.client.api
{
    internal interface IRepositoryCallback
    {
        void OnFlagStored(string identifier);
        void OnFlagDeleted(string identifier);
        void OnSegmentStored(string identifier);
        void OnSegmentDeleted(string identifier);
    }
    internal interface IRepository
    {
        void SetFlag(string identifier, FeatureConfig featureConfig);
        void SetSegment(string identifier, Segment segment);


        FeatureConfig GetFlag(string identifier);
        Segment GetSegment(string identifier);
        IEnumerable<string> FindFlagsBySegment(string identifier);

        void DeleteFlag(string identifier);
        void DeleteSegment(string identifier);
    }

    internal class StorageRepository : IRepository
    {
        private ICache cache;
        private IStore store;
        private IRepositoryCallback callback;
        public StorageRepository(ICache cache, IStore store, IRepositoryCallback callback)
        {
            this.cache = cache;
            this.store = store;
            this.callback = callback;
        }

        private string FlagKey(string identifier) {  return "flags/" + identifier; }
        private string SegmentKey(string identifier) { return "segments/" + identifier; }

        public FeatureConfig GetFlag(string identifier)
        {
            return GetFlag(identifier, true);
        }
        public Segment GetSegment(string identifier)
        {
            return GetSegment(identifier, true);
        }
        IEnumerable<string> IRepository.FindFlagsBySegment(string segment)
        {
            List<string> features = new List<string>();
            ICollection<string> keys = this.store != null ? this.store.Keys() : this.cache.Keys();
            foreach( string key in keys)
            {
                FeatureConfig feature = GetFlag(key);
                if(feature != null)
                {
                    foreach( ServingRule rule in feature.Rules)
                    {
                        foreach (Clause clause in rule.Clauses)
                        {
                            if(clause.Op.Equals("segmentMatch") && clause.Values.Contains(segment))
                            {
                                features.Add(feature.Feature);
                            }
                        }
                    }
                }

            }
            return features;
        }
        public void DeleteFlag(string identifier)
        {
            string key = FlagKey(identifier);
            if (store != null)
            {
                store.Delete(key);

            }
            this.cache.Delete(key);
            if (this.callback != null)
            {
                this.callback.OnFlagDeleted(identifier);
            }
        }

        public void DeleteSegment(string identifier)
        {
            string key = SegmentKey(identifier);
            if (store != null)
            {
                store.Delete(key);

            }
            this.cache.Delete(key);
            if (this.callback != null)
            {
                this.callback.OnSegmentDeleted(identifier);
            }
        }
        private Object GetCache(string key, bool updateCache)
        {
            Object item = this.cache.Get(key);
            if (item != null)
            {
                return item;
            }
            if (this.store != null)
            {
                item = this.store.Get(key);
                if (updateCache && item != null)
                {
                    this.cache.Set(key, item);
                }
            }
            return item;
        }
        private FeatureConfig GetFlag( string identifer, bool updateCache)
        {
            string key = FlagKey(identifer);
            return (FeatureConfig)GetCache(key, updateCache);
        }
        private Segment GetSegment(string identifer, bool updateCache)
        {
            string key = SegmentKey(identifer);
            return (Segment)GetCache(key, updateCache);
        }
        void IRepository.SetFlag(string identifier, FeatureConfig featureConfig)
        {
            FeatureConfig current = GetFlag(identifier, false);
            if( current != null && current.Version > featureConfig.Version )
            {
                return;
            }

            Update(FlagKey(identifier), featureConfig);

            if (this.callback != null)
            {
                this.callback.OnFlagStored(identifier);
            }
        }
        void IRepository.SetSegment(string identifier, Segment segment)
        {
            Segment current = GetSegment(identifier, false);
            if (current != null && current.Version > segment.Version)
            {
                return;
            }

            Update(SegmentKey(identifier), segment);

            if (this.callback != null)
            {
                this.callback.OnSegmentStored(identifier);
            }
        }

        private void Update(string key, Object value)
        {
            if (this.store == null)
            {
                cache.Set(key, value);
            }
            else
            {
                store.Set(key, value);
                cache.Delete(key);
            }
        }
    }
}
