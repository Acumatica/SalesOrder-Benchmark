using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Acumatica.Benchmark
{
    public class FixedStack<T>
    {
        private int _maximum;
        private List<T> items = new List<T>();

        public FixedStack(int maximum)
        {
            _maximum = maximum;
        }

        public void Push(T item)
        {
            if(items.Count >= _maximum)
            {
                items.RemoveAt(0);
            }

            items.Add(item);
        }

        public T PeekAt(int position)
        {
            int index = items.Count - position - 1;
            if (index >= 0 && index < items.Count)
            { 
                return items[index];
            }
            else
            {
                return default(T);
            }
        }

        public T Pop()
        {
            if (items.Count > 0)
            {
                T temp = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                return temp;
            }
            else
            { 
                return default(T);
            }
        }
    }
}
