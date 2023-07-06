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
        public static void Append(this IDictionary dictionary, IList keys, IList values)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                dictionary.Add(keys[i], values[i]);
            }
        }
    }
}
