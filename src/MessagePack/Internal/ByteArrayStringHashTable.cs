﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace MessagePack.Internal
{
    // like ArraySegment<byte> hashtable.
    // Add is safe for construction phase only and requires capacity(does not do rehash)
    // and specialized for internal use(nongenerics, TValue is int)

    // internal, but code generator requires this class
    public class ByteArrayStringHashTable : IEnumerable<KeyValuePair<string, int>>
    {
        Entry[][] buckets; // immutable array
        int indexFor;

        public ByteArrayStringHashTable(int capacity)
            : this(capacity, 0.42f) // default: 0.75f -> 0.42f
        {
        }

        public ByteArrayStringHashTable(int capacity, float loadFactor)
        {
            var tableSize = CalculateCapacity(capacity, loadFactor);
            this.buckets = new Entry[tableSize][];
            this.indexFor = buckets.Length - 1;
        }

        public void Add(string key, int value)
        {
            if (!TryAddInternal(Encoding.UTF8.GetBytes(key), value))
            {
                throw new ArgumentException("Key was already exists. Key:" + key);
            }
        }

        public void Add(byte[] key, int value)
        {
            if (!TryAddInternal(key, value))
            {
                throw new ArgumentException("Key was already exists. Key:" + key);
            }
        }

        bool TryAddInternal(byte[] key, int value)
        {
            var h = ByteArrayGetHashCode(key, 0, key.Length);
            var entry = new Entry { Key = key, Value = value };

            var array = buckets[h & (indexFor)];
            if (array == null)
            {
                buckets[h & (indexFor)] = new[] { entry };
            }
            else
            {
                // check duplicate
                for (int i = 0; i < array.Length; i++)
                {
                    var e = array[i].Key;
                    if (ByteArrayEquals(key, 0, key.Length, e))
                    {
                        return false;
                    }
                }

                var newArray = new Entry[array.Length + 1];
                Array.Copy(array, newArray, array.Length);
                array = newArray;
                array[array.Length - 1] = entry;
                buckets[h & (indexFor)] = array;
            }

            return true;
        }

        public bool TryGetValue(ArraySegment<byte> key, out int value)
        {
            var table = buckets;
            var hash = ByteArrayGetHashCode(key.Array, key.Offset, key.Count);
            var entry = table[hash & indexFor];

            if (entry == null) goto NOT_FOUND;

            {
#if NETSTANDARD1_4
                ref var v = ref entry[0];
#else
                var v = entry[0];
#endif
                if (ByteArrayEquals(key.Array, key.Offset, key.Count, v.Key))
                {
                    value = v.Value;
                    return true;
                }
            }

            for (int i = 1; i < entry.Length; i++)
            {
#if NETSTANDARD1_4
                ref var v = ref entry[i];
#else
                var v = entry[i];
#endif
                if (ByteArrayEquals(key.Array, key.Offset, key.Count, v.Key))
                {
                    value = v.Value;
                    return true;
                }
            }

            NOT_FOUND:
            value = default(int);
            return false;
        }

#if NETSTANDARD1_4
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        static uint ByteArrayGetHashCode(byte[] x, int offset, int count)
        {
#if NETSTANDARD1_4
            // use FarmHash is better?  https://github.com/google/farmhash
            if (x == null) return 0;
            return FarmHash.Hash32(x, offset, count);
#else

            // borrow from Roslyn's ComputeStringHash, calculate FNV-1a hash
            // http://source.roslyn.io/#Microsoft.CodeAnalysis.CSharp/Compiler/MethodBodySynthesizer.Lowered.cs,26

            uint hash = 0;
            if (x != null)
            {
                hash = 2166136261u; // hash = FNV_offset_basis

                var i = offset;
                var max = i + count;
                goto start;

                again:
                hash = unchecked((x[i] ^ hash) * 16777619); // hash = hash XOR byte_of_data, hash = hash × FNV_prime
                i = i + 1;

                start:
                if (i < max)
                {
                    goto again;
                }
            }

            return hash;

#endif
        }

#if NETSTANDARD1_4
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        static bool ByteArrayEquals(byte[] x, int xOffset, int xCount, byte[] y) // y is always 0 - length
        {
            // does not do null check for array.
            if (xCount != y.Length) return false;
            
            // reduce y's array bound check.
            for (int i = 0; i < y.Length; i++)
            {
                if (x[xOffset++] != y[i]) return false;
            }

            return true;
        }

        static int CalculateCapacity(int collectionSize, float loadFactor)
        {
            var initialCapacity = (int)(((float)collectionSize) / loadFactor);
            var capacity = 1;
            while (capacity < initialCapacity)
            {
                capacity <<= 1;
            }

            if (capacity < 8)
            {
                return 8;
            }

            return capacity;
        }

        // only for Debug use
        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        {
            var b = this.buckets;

            foreach (var item in b)
            {
                if (item == null) continue;
                foreach (var item2 in item)
                {
                    yield return new KeyValuePair<string, int>(Encoding.UTF8.GetString(item2.Key), item2.Value);
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        struct Entry
        {
            public byte[] Key;
            public int Value;
        }
    }
}