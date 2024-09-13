﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessObject.Entities
{
    public class Subject
    {
        [Key]
        public Guid ID { get; set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; set; }

        [Required]
        [MaxLength(250)]
        public string Grade { get; set; }

        [Required]
        public bool IsActive { get; set; }

        // Navigation properties
        public virtual ICollection<ComponentScore> ComponentScores { get; set; }
        public virtual ICollection<LessonPlans> LessonPlans { get; set; }
        public virtual ICollection<Schedule> Schedules { get; set; }
    }
}
