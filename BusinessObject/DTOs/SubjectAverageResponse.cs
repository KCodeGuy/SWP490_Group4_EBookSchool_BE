using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessObject.DTOs
{
    public class SubjectAverageResponse
    {
        public string Subject { get; set; }
        public string AverageWholeYear { get; set; }
        public string AverageSemester1 { get; set; }
        public string AverageSemester2 { get; set; }
    }
}
