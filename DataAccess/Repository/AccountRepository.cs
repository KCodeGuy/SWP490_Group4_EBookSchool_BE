﻿using Azure.Core;
using BusinessObject.DTOs;
using BusinessObject.Entities;
using BusinessObject.Exceptions;
using BusinessObject.Interfaces;
using BusinessObject.IServices;
using ClosedXML.Excel;
using DataAccess.Context;
using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DataAccess.Repository
{
    public class AccountRepository : IAccountRepository
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly IImageService _imageService;

        public AccountRepository(IConfiguration configuration, ApplicationDbContext context, IImageService imageService)
        {
            _configuration = configuration;
            _context = context;
            _imageService = imageService;
        }

        public async Task<LoginResponse> Login(LoginRequest request)
        {
            List<SchoolYear> schoolYears = await _context.SchoolYears
                .Include(s => s.Classes)
                .OrderBy(s => s.Name)
                .ToListAsync();

            Account accountExist = await _context.Accounts
                .Include(a => a.User)
                .Include(a => a.AccountRoles)
                .ThenInclude(a => a.Role)
                .ThenInclude(a => a.RolePermissions)
                .ThenInclude(a => a.Permission)
                .Include(a => a.AccountPermissions)
                .ThenInclude(a => a.Permission)
                .FirstOrDefaultAsync(a => a.Username.ToLower()
                .Equals(request.Username.ToLower()) && a.IsActive);

            if (accountExist == null)
            {
                AccountStudent accountStudentExist = await _context.AccountStudents
                .Include(a => a.Student)
                .Include(a => a.Role)
                .ThenInclude(a => a.RolePermissions)
                .ThenInclude(a => a.Permission)
                .FirstOrDefaultAsync(a => a.Username.ToLower()
                .Equals(request.Username.ToLower()) && a.IsActive)
                ?? throw new ArgumentException("Tên đăng nhập hoặc tài khoản không chính xác");

                if (accountStudentExist.RoleID == 6)
                {
                    schoolYears = await _context.SchoolYears
                                        .Include(s => s.Classes)
                                        .ThenInclude(c => c.StudentClasses)
                                        .Where(s => s.Classes.Any(c => c.StudentClasses.Any(sc => sc.StudentID.ToLower().Equals(accountStudentExist.ID.Substring(2).ToLower()))))
                                        .ToListAsync();
                } 
                else
                {
                    schoolYears = await _context.SchoolYears
                                        .Include(s => s.Classes)
                                        .ThenInclude(c => c.StudentClasses)
                                        .Where(s => s.Classes.Any(c => c.StudentClasses.Any(sc => sc.StudentID.ToLower().Equals(accountStudentExist.ID.ToLower()))))
                                        .ToListAsync();
                }

                var filteredSchoolYears = new List<SchoolYear>();
                string idFilter = accountStudentExist.RoleID == 6 ? accountStudentExist.ID.Substring(2).ToLower() : accountStudentExist.ID.ToLower();

                foreach (var schoolYear in schoolYears)
                {
                    var filteredSchoolYear = new SchoolYear
                    {
                        ID = schoolYear.ID,
                        Name = schoolYear.Name,
                        Classes = schoolYear.Classes
                            .Where(c => c.StudentClasses.Any(sc => sc.StudentID.ToLower() == idFilter))
                            .Select(c => new Classes
                            {
                                ID = c.ID,
                                Classroom = c.Classroom,
                                IsActive = c.IsActive,
                                StudentClasses = c.StudentClasses
                                    .Where(sc => sc.StudentID.ToLower() == idFilter)
                                    .ToList()
                            })
                            .ToList()
                    };

                    filteredSchoolYears.Add(filteredSchoolYear);
                }

                if (!accountStudentExist.IsActive)
                {
                    throw new ArgumentException("Tài khoản chờ xác nhận");
                }

                if (!BCrypt.Net.BCrypt.Verify(request.Password, accountStudentExist.Password))
                {
                    throw new ArgumentException("Tên đăng nhập hoặc tài khoản không chính xác");
                }

                List<string> roleStudents = new();

                foreach (var item1 in accountStudentExist.Role.RolePermissions)
                {
                    roleStudents.Add(item1.Permission.Name);
                }

                string refreshTokenS = GenerateRefreshToken();

                accountStudentExist.RefreshToken = refreshTokenS;
                accountStudentExist.RefreshTokenExpires = DateTime.Now.AddDays(30);

                await _context.SaveChangesAsync();

                RegisterResponse user = new();

                if (accountStudentExist.RoleID == 6)
                {
                    user = new RegisterResponse()
                    {
                        Id = accountStudentExist.ID,
                        Username = accountStudentExist.Username,
                        Address = accountStudentExist.Student.Address,
                        Email = accountStudentExist.Student.Email,
                        Fullname = accountStudentExist.Student.Fullname,
                        Phone = accountStudentExist.Student.Phone,
                        Avatar = accountStudentExist.Student.Avatar
                    };
                }
                else
                {
                    user = new RegisterResponse()
                    {
                        Id = accountStudentExist.ID,
                        Username = accountStudentExist.Username,
                        Address = accountStudentExist.Student.Address,
                        Email = accountStudentExist.Student.Email,
                        Fullname = accountStudentExist.Student.Fullname,
                        Phone = accountStudentExist.Student.Phone,
                        Avatar = accountStudentExist.Student.Avatar
                    };
                }

                LoginResponse loginResponseS = new LoginResponse()
                {
                    User = user,
                    Permissions = roleStudents,
                    Roles = new List<string>() { accountStudentExist.Role.Name },
                    SchoolYears = filteredSchoolYears.Select(s => s.Name).Order().ToList(),
                    Classes = filteredSchoolYears.ToDictionary(
                        item => item.Name,
                        item => item.Classes.Where(c => c.IsActive)
                        .OrderBy(c => c.Classroom)
                        .Select(c => new Dictionary<string, string>
                        {
                            { "ID", c.ID.ToString() },
                            { "Classroom", c.Classroom }
                        }).ToList()
                )
                };

                loginResponseS.AccessToken = CreateToken(loginResponseS, 60 * 60 * 24 * 30, roleStudents);
                loginResponseS.RefreshToken = refreshTokenS;

                return loginResponseS;
            }

            if (!accountExist.IsActive)
            {
                throw new ArgumentException("Tài khoản chờ xác nhận");
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, accountExist.Password))
            {
                throw new ArgumentException("Tên đăng nhập hoặc tài khoản không chính xác");
            }

            List<string> roles = new();

            foreach (var item in accountExist.AccountRoles)
            {
                foreach (var item1 in item.Role.RolePermissions)
                {
                    roles.Add(item1.Permission.Name);
                }
            }

            foreach (var item in accountExist.AccountPermissions)
            {
                roles.Add(item.Permission.Name);
            }

            string refreshToken = GenerateRefreshToken();

            accountExist.RefreshToken = refreshToken;
            accountExist.RefreshTokenExpires = DateTime.Now.AddDays(30);

            await _context.SaveChangesAsync();

            LoginResponse loginResponse = new LoginResponse()
            {
                User = new RegisterResponse()
                {
                    Id = accountExist.ID,
                    Username = accountExist.Username,
                    Address = accountExist.User.Address,
                    Email = accountExist.User.Email,
                    Fullname = accountExist.User.Fullname,
                    Phone = accountExist.User.Phone,
                    Avatar = accountExist.User.Avatar
                },
                Permissions = roles,
                Roles = accountExist.AccountRoles.Select(a => a.Role.Name).ToList(),
                SchoolYears = schoolYears.Select(s => s.Name).Order().ToList(),
                Classes = schoolYears.ToDictionary(
                            item => item.Name,
                            item => item.Classes.Where(c => c.IsActive)
                            .OrderBy( c => c.Classroom)
                            .Select(c => new Dictionary<string, string>
                            {
                                { "ID", c.ID.ToString() },
                                { "Classroom", c.Classroom }
                            }).ToList()
                        )
            };

            loginResponse.AccessToken = CreateToken(loginResponse, 60 * 60 * 24 * 30, roles);
            loginResponse.RefreshToken = refreshToken;

            return loginResponse;
        }

        public async Task<LoginResponse> RefreshToken(string accessToken, string refreshToken)
        {
            List<SchoolYear> schoolYears = await _context.SchoolYears
                .Include(s => s.Classes)
                .OrderBy(s => s.Name)
                .ToListAsync();

            string accountID = GetIdFromExpiredToken(accessToken);

            Account accountExist = await _context.Accounts
                .Include(a => a.User)
                .Include(a => a.AccountRoles)
                .ThenInclude(a => a.Role)
                .ThenInclude(a => a.RolePermissions)
                .ThenInclude(a => a.Permission)
                .Include(a => a.AccountPermissions)
                .ThenInclude(a => a.Permission)
                .FirstOrDefaultAsync(a => a.ID.Equals(accountID) && a.IsActive);

            if (accountExist == null)
            {
                AccountStudent accountStudentExist = await _context.AccountStudents
                .Include(a => a.Student)
                .Include(a => a.Role)
                .ThenInclude(a => a.RolePermissions)
                .ThenInclude(a => a.Permission)
                .FirstOrDefaultAsync(a => a.ID.Equals(accountID) && a.IsActive)
                ?? throw new ArgumentException("AccessToken không chính xác");

                schoolYears = await _context.SchoolYears
                                    .Include(s => s.Classes)
                                    .ThenInclude(c => c.StudentClasses)
                                    .Where(s => s.Classes.Any(c => c.StudentClasses.Any(sc => sc.StudentID.ToLower().Equals(accountStudentExist.ID.ToLower()))))
                                    .ToListAsync();

                var filteredSchoolYears = new List<SchoolYear>();

                foreach (var schoolYear in schoolYears)
                {
                    var filteredSchoolYear = new SchoolYear
                    {
                        ID = schoolYear.ID,
                        Name = schoolYear.Name,
                        Classes = schoolYear.Classes
                            .Where(c => c.StudentClasses.Any(sc => sc.StudentID.ToLower() == accountStudentExist.ID.ToLower()))
                            .Select(c => new Classes
                            {
                                ID = c.ID,
                                Classroom = c.Classroom,
                                IsActive = c.IsActive,
                                StudentClasses = c.StudentClasses
                                    .Where(sc => sc.StudentID.ToLower() == accountStudentExist.ID.ToLower())
                                    .ToList()
                            })
                            .ToList()
                    };

                    filteredSchoolYears.Add(filteredSchoolYear);
                }

                List<string> roleStudents = new();

                foreach (var item1 in accountStudentExist.Role.RolePermissions)
                {
                    roleStudents.Add(item1.Permission.Name);
                }

                string refreshTokenS = GenerateRefreshToken();

                accountStudentExist.RefreshToken = refreshTokenS;
                accountStudentExist.RefreshTokenExpires = DateTime.Now.AddDays(30);

                await _context.SaveChangesAsync();

                LoginResponse loginResponseS = new LoginResponse()
                {
                    User = new RegisterResponse()
                    {
                        Id = accountStudentExist.ID,
                        Username = accountStudentExist.Username,
                        Address = accountStudentExist.Student.Address,
                        Email = accountStudentExist.Student.Email,
                        Fullname = accountStudentExist.Student.Fullname,
                        Phone = accountStudentExist.Student.Phone,
                        Avatar = accountStudentExist.Student.Avatar
                    },
                    Permissions = roleStudents,
                    Roles = new List<string>() { accountStudentExist.Role.Name },
                    SchoolYears = filteredSchoolYears.Select(s => s.Name).Order().ToList(),
                    Classes = filteredSchoolYears.ToDictionary(
                        item => item.Name,
                        item => item.Classes.Where(c => c.IsActive)
                        .OrderBy(c => c.Classroom)
                        .Select(c => new Dictionary<string, string>
                        {
                            { "ID", c.ID.ToString() },
                            { "Classroom", c.Classroom }
                        }).ToList()
                )
                };

                loginResponseS.AccessToken = CreateToken(loginResponseS, 60 * 60 * 24 * 30, roleStudents);
                loginResponseS.RefreshToken = refreshTokenS;

                return loginResponseS;
            }

            if (accountExist.RefreshToken == null)
            {
                throw new ArgumentException("RefreshToken quá hạn");
            }

            string newRefreshToken = GenerateRefreshToken();

            TimeSpan? timeSpan = accountExist.RefreshTokenExpires - DateTime.Now;
            if (accountExist.RefreshToken.Equals(refreshToken) && timeSpan.HasValue && timeSpan.Value.TotalDays < 30)
            {


                List<string> roles = new();

                foreach (var item in accountExist.AccountRoles)
                {
                    foreach (var item1 in item.Role.RolePermissions)
                    {
                        roles.Add(item1.Permission.Name);
                    }
                }

                foreach (var item in accountExist.AccountPermissions)
                {
                    roles.Add(item.Permission.Name);
                }

                accountExist.RefreshToken = newRefreshToken;
                accountExist.RefreshTokenExpires = DateTime.Now.AddDays(30);

                await _context.SaveChangesAsync();

                LoginResponse loginResponse = new LoginResponse()
                {
                    User = new RegisterResponse()
                    {
                        Id = accountExist.ID,
                        Username = accountExist.Username,
                        Address = accountExist.User.Address,
                        Email = accountExist.User.Email,
                        Fullname = accountExist.User.Fullname,
                        Phone = accountExist.User.Phone,
                        Avatar = accountExist.User.Avatar
                    },
                    Permissions = roles,
                    Roles = accountExist.AccountRoles.Select(a => a.Role.Name).ToList(),
                    SchoolYears = schoolYears.Select(s => s.Name).Order().ToList(),
                    Classes = schoolYears.ToDictionary(
                                item => item.Name,
                                item => item.Classes.Where(c => c.IsActive)
                                .OrderBy(c => c.Classroom)
                                .Select(c => new Dictionary<string, string>
                                {
                                { "ID", c.ID.ToString() },
                                { "Classroom", c.Classroom }
                                }).ToList()
                            )
                };

                loginResponse.AccessToken = CreateToken(loginResponse, 60 * 60 * 24 * 30, roles);
                loginResponse.RefreshToken = refreshToken;

                return loginResponse;
            }

            throw new ArgumentException("RefreshToken quá hạn");
        }

        public async Task RegisterTeacher(RegisterTeacherRequest request)
        {
            Account accountIDExist = await _context.Accounts.FirstOrDefaultAsync(a => a.ID.ToLower()
            .Equals(request.Username.ToLower().Trim()));

            if (accountIDExist != null)
            {
                throw new ArgumentException("ID đã được sử dụng");
            }

            Account accountExist = await _context.Accounts.FirstOrDefaultAsync(a => a.Username.ToLower()
            .Equals(request.Username.ToLower().Trim()));

            if (accountExist != null)
            {
                throw new ArgumentException("Tên đăng nhập đã được sử dụng");
            }

            AccountStudent studentExist = await _context.AccountStudents.FirstOrDefaultAsync(a => a.Username.ToLower()
            .Equals(request.Username.ToLower().Trim()));

            if (studentExist != null)
            {
                throw new ArgumentException("Tên đăng nhập đã được sử dụng");
            }

            Guid guid = Guid.NewGuid();
            string accountID = request.Username.Trim();

            string avt = "https://cantho.fpt.edu.vn/Data/Sites/1/media/logo-moi.png";

            if (request.Avatar != null)
            {
                avt = await _imageService.UploadImage(request.Avatar, "Avatars");
            }

            User user = new()
            {
                ID = guid,
                Address = request.Address.Trim(),
                Avatar = avt,
                Email = request.Email.Trim(),
                Birthday = request.Birthday,
                Fullname = request.Fullname.Trim(),
                Gender = request.Gender.Trim(),
                IsBachelor = request.IsBachelor,
                IsDoctor = request.IsDoctor,
                IsMaster = request.IsMaster,
                IsProfessor = request.IsProfessor,
                Nation = request.Nation.Trim(),
                Phone = request.Phone.Trim(),
            };

            await _context.Users.AddAsync(user);

            Account account = new()
            {
                ID = accountID,
                IsActive = true,
                Password = BCrypt.Net.BCrypt.HashPassword(request.Password.Trim()),
                Username = request.Username.Trim(),
                UserID = guid,
                RefreshToken = "",
                RefreshTokenExpires = DateTime.Now
            };

            await _context.Accounts.AddAsync(account);

            if (request.Roles != null && request.Roles.Count > 0)
            {
                List<AccountRole> roles = new();

                foreach (var role in request.Roles)
                {
                    Role role1 = await _context.Roles.FirstOrDefaultAsync(r => r.Name.ToLower().Equals(role.ToLower()));

                    if (role1 != null)
                    {
                        roles.Add(new()
                        {
                            AccountID = accountID,
                            RoleID = role1.ID
                        });
                    }
                }

                await _context.AccountRoles.AddRangeAsync(roles);
            }

            if (request.Permissions != null && request.Permissions.Count > 0)
            {

                List<AccountPermission> permissions = new();

                foreach (var role in request.Permissions)
                {
                    Permission per = await _context.Permissions.FirstOrDefaultAsync(r => r.Name.ToLower().Equals(role.ToLower()));

                    if (per != null)
                    {
                        permissions.Add(new()
                        {
                            AccountID = accountID,
                            PermissionID = per.ID
                        });
                    }
                }

                await _context.AccountPermissions.AddRangeAsync(permissions);
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateTeacher(string accountID, UpdateTeacherRequest request)
        {
            Account accountExist = await _context.Accounts
                .Include(a => a.User)
                .Include(a => a.AccountPermissions)
                .ThenInclude(a => a.Permission)
                .Include(a => a.AccountRoles)
                .ThenInclude(a => a.Role)
                .FirstOrDefaultAsync(a => a.ID.ToLower()
                .Equals(accountID.ToLower().Trim())) ?? throw new NotFoundException("Tài khoản không tồn tại");

            string avt = accountExist.User.Avatar;

            if (request.Avatar != null)
            {
                avt = await _imageService.UploadImage(request.Avatar, "Avatars");
            }

            if (!string.IsNullOrEmpty(request.Password))
                accountExist.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
            accountExist.User.Address = request.Address;
            accountExist.User.Fullname = request.Fullname;
            accountExist.User.Birthday = request.Birthday;
            accountExist.User.Email = request.Email;
            accountExist.User.Phone = request.Phone;
            accountExist.User.Gender = request.Gender;
            accountExist.User.Nation = request.Nation;
            accountExist.User.IsDoctor = request.IsDoctor;
            accountExist.User.IsBachelor = request.IsBachelor;
            accountExist.User.IsMaster = request.IsMaster;
            accountExist.User.IsProfessor = request.IsProfessor;
            accountExist.User.Avatar = avt;

            if (request.Roles != null && request.Roles.Count > 0)
            {
                _context.AccountRoles.RemoveRange(accountExist.AccountRoles);
            }

            if (request.Permissions != null && request.Permissions.Count > 0)
            {
                _context.AccountPermissions.RemoveRange(accountExist.AccountPermissions);
            }

            List<AccountRole> roles = new();

            if (request.Roles != null && request.Roles.Count > 0)
                foreach (var role in request.Roles)
                {
                    Role role1 = await _context.Roles.FirstOrDefaultAsync(r => r.Name.ToLower().Equals(role.ToLower()));

                    if (role1 != null)
                    {
                        roles.Add(new()
                        {
                            AccountID = accountID,
                            RoleID = role1.ID
                        });
                    }
                }

            await _context.AccountRoles.AddRangeAsync(roles);

            List<AccountPermission> permissions = new();

            if (request.Permissions != null && request.Permissions.Count > 0)
                foreach (var role in request.Permissions)
                {
                    Permission per = await _context.Permissions.FirstOrDefaultAsync(r => r.Name.ToLower().Equals(role.ToLower()));

                    if (per != null)
                    {
                        permissions.Add(new()
                        {
                            AccountID = accountID,
                            PermissionID = per.ID
                        });
                    }
                }

            await _context.AccountPermissions.AddRangeAsync(permissions);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<RegisterResponse>> GetTeachers()
        {
            return await _context.Accounts
                .AsNoTracking()
                .Include(a => a.User)
                .Where(a => a.IsActive)
                .Select(item => new RegisterResponse()
                {
                    Id = item.ID,
                    Fullname = item.User.Fullname,
                    Address = item.User.Address,
                    Avatar = item.User.Avatar,
                    Email = item.User.Email,
                    Phone = item.User.Phone,
                    Username = item.Username
                })
                .OrderBy(a => a.Fullname)
                .ToListAsync();
        }

        public async Task<TeacherResponse> GetTeacher(string accountID)
        {
            Account account = await _context.Accounts
                .AsNoTracking()
                .Include(a => a.User)
                .Include(a => a.AccountPermissions)
                .Include(a => a.AccountRoles)
                .ThenInclude(a => a.Role)
                .ThenInclude(a => a.RolePermissions)
                .ThenInclude(a => a.Permission)
                .FirstOrDefaultAsync(a => a.ID.ToLower()
                .Equals(accountID.ToLower()) && a.IsActive);

            if (account == null)
            {
                throw new NotFoundException("Tài khoản không tồn tại");
            }

            List<string> permission = account.AccountPermissions.Select(item => item.Permission.Name).ToList();

            foreach (var item in account.AccountRoles)
            {
                foreach (var item1 in item.Role.RolePermissions)
                {
                    permission.Add(item1.Permission.Name);
                }
            }

            return new TeacherResponse()
            {
                Username = account.Username,
                Address = account.User.Address,
                Avatar = account.User.Avatar,
                Birthday = account.User.Birthday,
                Email = account.User.Email,
                Fullname = account.User.Fullname,
                Gender = account.User.Gender,
                ID = account.ID,
                IsBachelor = account.User.IsBachelor,
                IsDoctor = account.User.IsDoctor,
                IsMaster = account.User.IsMaster,
                IsProfessor = account.User.IsProfessor,
                Nation = account.User.Nation,
                Phone = account.User.Phone,
                Roles = account.AccountRoles.Select(item => item.Role.Name).ToList(),
                Permissions = permission
            };
        }

        public async Task DeleteTeacher(string accountID)
        {
            Account account = await _context.Accounts
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.ID.ToLower()
                .Equals(accountID.ToLower()));

            if (account == null)
            {
                throw new NotFoundException("Tài khoản không tồn tại");
            }

            account.IsActive = false;

            await _context.SaveChangesAsync();
        }

        public async Task RegisterStudent(RegisterStudentRequest request)
        {
            string newID = CreateNewAccountId();
            string newUsername = CreateUsername(request.Fullname, newID);
            Guid userID = Guid.NewGuid();

            string avt = "https://cantho.fpt.edu.vn/Data/Sites/1/media/logo-moi.png";

            if (request.Avatar != null)
            {
                avt = await _imageService.UploadImage(request.Avatar, "Avatars");
            }

            Student student = new()
            {
                ID = userID,
                Address = request.Address,
                Avatar = avt,
                Birthday = request.Birthday,
                Birthplace = request.Birthplace,
                Email = request.Email,
                Fullname = request.Fullname,
                Gender = request.Gender,
                HomeTown = request.HomeTown,
                IsMartyrs = request.IsMartyrs,
                Nation = request.Nation,
                Phone = request.Phone,
                FatherFullName = request.FatherFullName,
                FatherPhone = request.FatherPhone,
                FatherProfession = request.FatherProfession,
                MotherFullName = request.MotherFullName,
                MotherPhone = request.MotherPhone,
                MotherProfession = request.MotherProfession
            };

            await _context.Students.AddAsync(student);

            AccountStudent studentAccount = new()
            {
                ID = newID,
                IsActive = true,
                Password = BCrypt.Net.BCrypt.HashPassword("aA@123"),
                RefreshToken = "",
                RefreshTokenExpires = DateTime.Now,
                RoleID = 2,
                UserID = userID,
                Username = newUsername,
            };

            await _context.AccountStudents.AddAsync(studentAccount);

            AccountStudent parentAccount = new()
            {
                ID = "PH" + newID,
                IsActive = true,
                Password = BCrypt.Net.BCrypt.HashPassword("aA@123"),
                RefreshToken = "",
                RefreshTokenExpires = DateTime.Now,
                RoleID = 2,
                UserID = userID,
                Username = "PH" + newUsername,
            };

            await _context.AccountStudents.AddAsync(parentAccount);

            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<RegisterResponse>> GetStudents()
        {
            return await _context.AccountStudents
                .AsNoTracking()
                .Include(a => a.Student)
                .Where(a => a.IsActive && !a.ID.StartsWith("PH"))
                .Select(item => new RegisterResponse()
                {
                    Id = item.ID,
                    Fullname = item.Student.Fullname,
                    Address = item.Student.Address,
                    Avatar = item.Student.Avatar,
                    Email = item.Student.Email,
                    Phone = item.Student.Phone,
                    Username = item.Username
                })
                .OrderBy(a => a.Id)
                .ToListAsync();
        }

        public async Task<StudentResponse> GetStudent(string accountID)
        {
            AccountStudent account = await _context.AccountStudents
                .AsNoTracking()
                .Include(a => a.Student)
                .FirstOrDefaultAsync(a => a.ID.ToLower()
                .Equals(accountID.ToLower()) && a.IsActive);

            if (account == null)
            {
                throw new NotFoundException("Tài khoản không tồn tại");
            }

            return new StudentResponse()
            {
                Username = account.Username,
                Address = account.Student.Address,
                Avatar = account.Student.Avatar,
                Birthday = account.Student.Birthday,
                Email = account.Student.Email,
                Fullname = account.Student.Fullname,
                Gender = account.Student.Gender,
                ID = account.ID,
                Nation = account.Student.Nation,
                Phone = account.Student.Phone,
                FatherFullName = account.Student.FatherFullName,
                Birthplace = account.Student.Birthplace,
                FatherPhone = account.Student.FatherPhone,
                FatherProfession = account.Student.FatherProfession,
                HomeTown = account.Student.HomeTown,
                IsMartyrs = account.Student.IsMartyrs,
                MotherFullName = account.Student.MotherFullName,
                MotherPhone = account.Student.MotherPhone,
                MotherProfession = account.Student.MotherProfession,
            };
        }

        public async Task DeleteStudent(string accountID)
        {
            AccountStudent account = await _context.AccountStudents
                .FirstOrDefaultAsync(a => a.ID.ToLower()
                .Equals(accountID.ToLower()));

            if (account == null)
            {
                throw new NotFoundException("Tài khoản không tồn tại");
            }

            account.IsActive = false;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateStudent(string accountID, UpdateStudentRequest request)
        {
            AccountStudent account = await _context.AccountStudents
                .Include(a => a.Student)
                .FirstOrDefaultAsync(a => a.ID.ToLower()
                .Equals(accountID.ToLower()));

            if (account == null)
            {
                throw new NotFoundException("Tài khoản không tồn tại");
            }

            string avt = account.Student.Avatar;

            if (request.Avatar != null)
            {
                avt = await _imageService.UploadImage(request.Avatar, "Avatars");
            }

            if (!string.IsNullOrEmpty(request.Password))
                account.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
            account.Student.Fullname = request.Fullname;
            account.Student.Address = request.Address;
            account.Student.Email = request.Email;
            account.Student.Phone = request.Phone;
            account.Student.Gender = request.Gender;
            account.Student.Birthday = request.Birthday;
            account.Student.Nation = request.Nation;
            account.Student.Birthplace = request.Birthplace;
            account.Student.HomeTown = request.HomeTown;
            account.Student.FatherFullName = request.FatherFullName;
            account.Student.FatherProfession = request.FatherProfession;
            account.Student.FatherPhone = request.FatherPhone;
            account.Student.MotherFullName = request.MotherFullName;
            account.Student.MotherProfession = request.MotherProfession;
            account.Student.MotherPhone = request.MotherPhone;
            account.Student.IsMartyrs = request.IsMartyrs;
            account.Student.Avatar = avt;

            await _context.SaveChangesAsync();

        }

        public async Task AddStudentByExcel(string accountID, ExcelRequest request)
        {
            Account account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ID.ToLower().Equals(accountID.ToLower())) ?? throw new NotFoundException("Tài khoản của bạn không tồn tại");

            IFormFile file = request.File;
            if (file != null && file.Length > 0)
            {
                using (var stream = new MemoryStream())
                {
                    file.CopyTo(stream);

                    stream.Position = 0;

                    using (var workbook = new XLWorkbook(stream))
                    {
                        foreach (var worksheet in workbook.Worksheets)
                        {
                            List<RegisterStudentRequest> registerStudents = new();

                            for (int row = 2; row <= worksheet.LastRowUsed().RowNumber(); row++)
                            {
                                var dateString = worksheet.Cell(row, 7).GetString();
                                DateTime birthday;
                                if (!DateTime.TryParseExact(dateString, new[] { "d/M/yyyy", "dd/MM/yyyy", "d/MM/yyyy", "dd/M/yyyy" }, null, System.Globalization.DateTimeStyles.None, out birthday))
                                {
                                    throw new ArgumentException($"Invalid date format for {dateString}");
                                }

                                var student = new RegisterStudentRequest()
                                {
                                    Fullname = worksheet.Cell(row, 2).GetString(),
                                    Address = worksheet.Cell(row, 3).GetString(),
                                    Email = worksheet.Cell(row, 4).GetString(),
                                    Phone = worksheet.Cell(row, 5).GetString(),
                                    Gender = worksheet.Cell(row, 6).GetString(),
                                    Birthday = birthday,
                                    Nation = worksheet.Cell(row, 8).GetString(),
                                    IsMartyrs = worksheet.Cell(row, 9).GetString().ToLower().Trim().Equals("có"),
                                    Birthplace = worksheet.Cell(row, 10).GetString(),
                                    HomeTown = worksheet.Cell(row, 11).GetString(),
                                    FatherFullName = worksheet.Cell(row, 12).GetString(),
                                    FatherPhone = worksheet.Cell(row, 13).GetString(),
                                    FatherProfession = worksheet.Cell(row, 14).GetString(),
                                    MotherFullName = worksheet.Cell(row, 15).GetString(),
                                    MotherPhone = worksheet.Cell(row, 16).GetString(),
                                    MotherProfession = worksheet.Cell(row, 17).GetString()
                                };

                                registerStudents.Add(student);
                            }

                            foreach (var student in registerStudents)
                            {
                                await RegisterStudent(student);
                            }
                        }
                    }
                }
            }
        }

        public async Task AddTeacherByExcel(string accountID, ExcelRequest request)
        {
            Account account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ID.ToLower().Equals(accountID.ToLower())) ?? throw new NotFoundException("Tài khoản của bạn không tồn tại");

            IFormFile file = request.File;
            if (file != null && file.Length > 0)
            {
                using (var stream = new MemoryStream())
                {
                    file.CopyTo(stream);

                    stream.Position = 0;

                    using (var workbook = new XLWorkbook(stream))
                    {
                        foreach (var worksheet in workbook.Worksheets)
                        {
                            List<RegisterTeacherRequest> registerTeachers = new();

                            for (int row = 2; row <= worksheet.LastRowUsed().RowNumber(); row++)
                            {
                                var dateString = worksheet.Cell(row, 8).GetString();
                                DateTime birthday;
                                if (!DateTime.TryParseExact(dateString, new[] { "d/M/yyyy", "dd/MM/yyyy", "d/MM/yyyy", "dd/M/yyyy" }, null, System.Globalization.DateTimeStyles.None, out birthday))
                                {
                                    throw new ArgumentException($"Invalid date format for {dateString}");
                                }

                                var teacher = new RegisterTeacherRequest()
                                {
                                    Username = worksheet.Cell(row, 2).GetString(),
                                    Fullname = worksheet.Cell(row, 3).GetString(),
                                    Address = worksheet.Cell(row, 4).GetString(),
                                    Email = worksheet.Cell(row, 5).GetString(),
                                    Phone = worksheet.Cell(row, 6).GetString(),
                                    Gender = worksheet.Cell(row, 7).GetString(),
                                    Birthday = birthday,
                                    Nation = worksheet.Cell(row, 9).GetString(),
                                    IsBachelor = worksheet.Cell(row, 10).GetString().ToLower().Trim().Equals("có"),
                                    IsMaster = worksheet.Cell(row, 11).GetString().ToLower().Trim().Equals("có"),
                                    IsDoctor = worksheet.Cell(row, 12).GetString().ToLower().Trim().Equals("có"),
                                    IsProfessor = worksheet.Cell(row, 13).GetString().ToLower().Trim().Equals("có"),
                                    Roles = new List<string>()
                                    {
                                        "Subject Teacher", "Homeroom Teacher"
                                    },
                                    Password = "aA@123",
                                };

                                registerTeachers.Add(teacher);
                            }

                            foreach (var teacher in registerTeachers)
                            {
                                await RegisterTeacher(teacher);
                            }
                        }
                    }
                }
            }
        }

        private string CreateNewAccountId()
        {
            var maxId = _context.AccountStudents.Where(a => a.ID.StartsWith("HS")).Max(a => a.ID);

            if (!string.IsNullOrEmpty(maxId))
            {
                var numericId = maxId.Substring(2);

                var number = int.Parse(numericId);

                number++;

                var newId = $"HS{number.ToString().PadLeft(4, '0')}";

                return newId;
            }

            return "HS0001";
        }

        private string CreateToken(LoginResponse user, int seconds, List<string> roles)
        {
            string issuer = _configuration["AppSettings:Issuer"];
            string audience = _configuration["AppSettings:Audience"];
            string secretKey = _configuration["AppSettings:SecretKey"];

            List<Claim> authClaims = new List<Claim>
            {
                new("ID", user.User.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            foreach (var role in roles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: authClaims,
                expires: DateTime.UtcNow.AddSeconds(seconds),
                signingCredentials: creds
            );

            var tokenHandler = new JwtSecurityTokenHandler();

            return tokenHandler.WriteToken(token);
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Base64UrlEncode(randomNumber);
            }
        }

        private string Base64UrlEncode(byte[] input)
        {
            var output = Convert.ToBase64String(input);
            output = output.Split('=')[0];
            output = output.Replace('+', '-');
            output = output.Replace('/', '_');
            return output;
        }

        private string GetIdFromExpiredToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_configuration["AppSettings:SecretKey"]);

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidIssuer = _configuration["AppSettings:Issuer"],
                ValidAudience = _configuration["AppSettings:Audience"],
                ValidateLifetime = false,
                ClockSkew = TimeSpan.Zero
            };

            SecurityToken validatedToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out validatedToken);
            var jwtToken = (JwtSecurityToken)validatedToken;

            var idClaim = principal.FindFirst("ID")?.Value;

            if (idClaim == null)
            {
                throw new SecurityTokenException("Invalid token claims");
            }

            return idClaim;
        }

        private ClaimsPrincipal ValidateToken(string token)
        {
            string secretKey = _configuration["AppSettings:SecretKey"];
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidIssuer = _configuration["AppSettings:Issuer"],
                ValidAudience = _configuration["AppSettings:Audience"],
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken validatedToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out validatedToken);

            return principal;
        }
        private string CreateUsername(string fullName, string suffix)
        {
            string RemoveDiacritics(string text)
            {
                var normalizedString = text.Normalize(NormalizationForm.FormD);
                var stringBuilder = new StringBuilder();

                foreach (var c in normalizedString)
                {
                    var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                    if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                    {
                        stringBuilder.Append(c);
                    }
                }

                return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
            }

            var nameParts = RemoveDiacritics(fullName).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (nameParts.Length < 2)
            {
                throw new ArgumentException("Tên đầy đủ phải có ít nhất hai phần.", nameof(fullName));
            }

            var firstName = nameParts.Last();
            var initials = nameParts.Take(nameParts.Length - 1)
                                    .Select(p => p[0].ToString().ToUpperInvariant());

            var username = $"{firstName}{string.Join("", initials)}{suffix}".ToUpperInvariant();

            return username;
        }

        public async Task Logout(string accountID)
        {
            Account account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ID.ToLower().Equals(accountID.ToLower()));

            if (account != null)
            {
                account.RefreshToken = "";
            } else
            {
                AccountStudent student = await _context.AccountStudents
                    .FirstOrDefaultAsync(a => a.ID.ToLower().Equals(accountID.ToLower()));

                if (student != null)
                {
                    student.RefreshToken = "";
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
