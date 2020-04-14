using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompressionApp
{
    public class Error
    {
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public Error() { }
        public Error(string message, Exception ex)
        {
            Message = message;
            Exception = ex;
        }
    }
}
