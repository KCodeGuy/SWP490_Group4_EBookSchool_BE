﻿using BusinessObject.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessObject.Interfaces
{
    public interface IClassesRepository
    {
        public Task<IEnumerable<ClassesResponse>> GetClasses();
        public Task<ClassResponse> GetClass(string className, string schoolYear);
        public Task AddClasses(string accountID, ClassesRequest request);
        public Task AddClassesByExcel(string accountID, ExcelRequest request);
        public Task UpdateClasses(string accountID, string classID, ClassesRequest request);
        public Task DeleteClasses(string accountID, string classID);
        public Task<byte[]> GenerateExcelFile(string className, string schoolYear);
    }
}
