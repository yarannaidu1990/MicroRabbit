using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MicroRabbit.Domain.Core.Events
{
    public abstract class Event
    {
        protected Event() { 
           TimeStamp = DateTime.Now;    
        }
        public DateTime TimeStamp { get; set; }
    }
}
