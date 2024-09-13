﻿using BusinessObject.IServices;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DataAccess.Services
{
    public class ImageService : IImageService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ImageService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<string> UploadImage(IFormFile file, string folder = "Images", string fileName = null)
        {
            if (file == null || file.Length == 0)
            {
                return "";
            }

            var folderName = Path.Combine("wwwroot", folder);
            var pathToSave = Path.Combine(Directory.GetCurrentDirectory(), folderName);

            if (!Directory.Exists(pathToSave))
            {
                Directory.CreateDirectory(pathToSave);
            }

            fileName = (fileName ?? Guid.NewGuid().ToString()) + Path.GetExtension(file.FileName);
            var fullPath = Path.Combine(pathToSave, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var currentUrl = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host}";
            var imageUrl = Path.Combine(currentUrl, folderName, fileName).Replace("\\", "/").Replace("/wwwroot", "");

            return imageUrl;
        }

    }
}
