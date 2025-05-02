using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioVisualizerWidget
{
    public static class DictionaryExtension
    {
        /// <summary>
        /// Appends the keys and values from the specified lists to the dictionary.
        /// </summary>
        /// <param name="dictionary">The dictionary to append to.</param>
        /// <param name="keys">The list of keys to append.</param>
        /// <param name="values">The list of values to append.</param>
        /// <exception cref="ArgumentNullException">Thrown if any of the parameters is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the keys and values lists have different counts.</exception>
        public static void Append(this IDictionary dictionary, IList keys, IList values)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));
            
            if (keys == null)
                throw new ArgumentNullException(nameof(keys));
            
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            
            if (keys.Count != values.Count)
                throw new ArgumentException("Keys and values must have the same count", nameof(values));
            
            try
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i] != null && !dictionary.Contains(keys[i]))
                    {
                        dictionary.Add(keys[i], values[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error appending to dictionary: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Appends the keys and values from the specified lists to the generic dictionary.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to append to.</param>
        /// <param name="keys">The list of keys to append.</param>
        /// <param name="values">The list of values to append.</param>
        /// <exception cref="ArgumentNullException">Thrown if any of the parameters is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the keys and values lists have different counts.</exception>
        public static void Append<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, IList<TKey> keys, IList<TValue> values)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));
            
            if (keys == null)
                throw new ArgumentNullException(nameof(keys));
            
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            
            if (keys.Count != values.Count)
                throw new ArgumentException("Keys and values must have the same count", nameof(values));
            
            try
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i] != null && !dictionary.ContainsKey(keys[i]))
                    {
                        dictionary.Add(keys[i], values[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error appending to dictionary: {ex.Message}", ex);
            }
        }
    }
}
