﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessObject.Entities
{
    public class Classes
    {
        [Key]
        public Guid ID { get; set; }

        [Required]
        [MaxLength(50)]
        public string TeacherID { get; set; }

        public string Classroom { get; set; }

        [Required]
        public Guid SchoolYearID { get; set; }

        [Required]
        public bool IsActive { get; set; }

        // Navigation properties
        [ForeignKey("TeacherID")]
        public virtual Account Teacher { get; set; }
        [ForeignKey("SchoolYearID")]
        public virtual SchoolYear SchoolYear { get; set; }
        public virtual ICollection<StudentClasses> StudentClasses { get; set; }
        public virtual ICollection<Schedule> Schedules { get; set; }
    }
}
