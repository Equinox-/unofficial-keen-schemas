using System.Collections.Generic;
using System.Xml;

namespace SchemaBuilder
{
    internal sealed class UnionFind<T>
    {
        private readonly Dictionary<T, T> _dictionary = new Dictionary<T, T>();

        public void Insert(T name) => _dictionary.Add(name, name);

        public void Union(T left, T right)
        {
            left = Find(left);
            right = Find(right);
            // Dumb union operation that ignores rank/size, but this code can be slow so whatever.
            _dictionary[left] = right;
        }

        public T Find(T name)
        {
            var root = name;
            while (true)
            {
                var parent = _dictionary[root];
                if (parent.Equals(root))
                    return root;
                var grandparent = _dictionary[parent];
                // Path halving.
                _dictionary[root] = grandparent;
                root = grandparent;
            }
        }
    }
}