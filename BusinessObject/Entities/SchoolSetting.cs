﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessObject.Entities
{
    public class SchoolSetting
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string SchoolName { get; set; }

        [Required]
        [MaxLength(250)]
        public string SchoolAddress { get; set; }

        [Required]
        [MaxLength(10)]
        public string SchoolPhone { get; set; }

        [Required]
        [MaxLength(100)]
        public string SchoolEmail { get; set; }

        [Required]
        [MaxLength(50)]
        public string SchoolLevel { get; set; }

        [ForeignKey("Account")]
        public string CreateBy { get; set; }

        [ForeignKey("CreateBy")]
        public virtual Account Account { get; set; }
    }
}
