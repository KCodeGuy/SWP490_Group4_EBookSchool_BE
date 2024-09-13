﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessObject.DTOs
{
    public class LoginResponse
    {
        public RegisterResponse User { get; set; }
        public string AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public List<string> Permissions { get; set; }
        public List<string> Roles { get; set; }
        public List<string> SchoolYears { get; set; }
        public Dictionary<string, List<Dictionary<string, string>>> Classes { get; set; }
    }
}
