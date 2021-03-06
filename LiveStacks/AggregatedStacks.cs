﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiveStacks
{
    struct AggregatedStack
    {
        public int ProcessID { get; set; }
        public ulong[] Addresses { get; set; }
        public int Count { get; set; }
    }

    struct OneStack : IEquatable<OneStack>
    {
        public ulong[] Addresses { get; set; }

        public override int GetHashCode()
        {
            // TODO Can this be sped up? E.g. using SIMD?

            int hc = Addresses.Length;
            for (int i = 0; i < Addresses.Length; ++i)
            {
                hc = unchecked((int)((ulong)hc * 37 + Addresses[i]));
            }
            return hc;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is OneStack))
                return false;

            OneStack other = (OneStack)obj;
            return Equals(other);
        }

        public bool Equals(OneStack other)
        {
            // TODO Can this be sped up? E.g. using SIMD?

            if (Addresses.Length != other.Addresses.Length)
                return false;

            for (int i = 0; i < Addresses.Length; ++i)
                if (Addresses[i] != other.Addresses[i])
                    return false;

            return true;
        }
    }

    class PidStacks
    {
        public int ProcessID { get; private set; }
        public ConcurrentDictionary<OneStack, int> CountedStacks { get; } = new ConcurrentDictionary<OneStack, int>();

        public PidStacks(int processID)
        {
            ProcessID = processID;
        }

        public void AddStack(ulong[] addresses)
        {
            var stack = new OneStack { Addresses = addresses };
            CountedStacks.AddOrUpdate(stack, 1, (_, existingCount) => existingCount + 1);
        }
    }

    /// <summary>
    /// Contains a summary of stack occurrences, aggregated per process and counted. This class
    /// is thread-safe.
    /// </summary>
    class AggregatedStacks
    {
        private ConcurrentDictionary<int, PidStacks> _pidStacks = new ConcurrentDictionary<int, PidStacks>();

        public void AddStack(int processID, ulong[] addresses)
        {
            _pidStacks.AddOrUpdate(processID,
                pid =>
                {
                    PidStacks stacks = new PidStacks(pid);
                    stacks.AddStack(addresses);
                    return stacks;
                },
                (_, existing) =>
                {
                    existing.AddStack(addresses);
                    return existing;
                });
        }

        /// <summary>
        /// Get the top stacks from the recorded trace.
        /// </summary>
        /// <param name="top">The number of stacks to return.</param>
        /// <param name="minSamples">The minimum number of samples a returned stack must have.</param>
        /// <returns>A dictionary that contains the top <see cref="top"/> stacks
        /// by number of occurrences.</returns>
        public List<AggregatedStack> TopStacks(int top, int minSamples)
        {
            return (from kvp in _pidStacks
                 from stacks in kvp.Value.CountedStacks
                 where stacks.Value >= minSamples
                 orderby stacks.Value descending
                 select new AggregatedStack { ProcessID = kvp.Key, Count = stacks.Value, Addresses = stacks.Key.Addresses }
                 ).Take(top).ToList();
        }

        public IDictionary<int, List<AggregatedStack>> AllStacksByProcess()
        {
            var result = new Dictionary<int, List<AggregatedStack>>();
            foreach (var kvp in _pidStacks)
            {
                List<AggregatedStack> stacks = new List<AggregatedStack>();
                foreach (var countedStack in kvp.Value.CountedStacks)
                {
                    stacks.Add(new AggregatedStack { Count = countedStack.Value, Addresses = countedStack.Key.Addresses });
                }
                result.Add(kvp.Key, stacks);
            }
            return result;
        }

        public void Clear()
        {
            _pidStacks.Clear();
        }
    }
}
