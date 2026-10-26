using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;

namespace DynamicApiGenerator.Generated
{
    public class GetProducts_f70552ResponseDto
    {
        public int productID { get; set; }
    }
    [ApiController]
    [Route("api/products/GetProductInfo")]
    public class GetProducts_f70552Controller : ControllerBase
    {
        private static List<GetProducts_f70552ResponseDto>? _cachedData;
        private readonly string _dataPath;

        public GetProducts_f70552Controller(IWebHostEnvironment env)
        {
            _dataPath = Path.Combine(env.ContentRootPath, "GeneratedApis", "data_GetProducts_f70552.json");
        }

        private List<GetProducts_f70552ResponseDto> LoadData()
        {
            if (_cachedData != null) return _cachedData;
            if (!System.IO.File.Exists(_dataPath)) return new List<GetProducts_f70552ResponseDto>();
            string json = System.IO.File.ReadAllText(_dataPath);
            _cachedData = JsonSerializer.Deserialize<List<GetProducts_f70552ResponseDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            return _cachedData;
        }

        [HttpGet]
        public IActionResult Handle([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var data = LoadData();
            var pagedItems = data.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new {
                TotalRecords = data.Count,
                CurrentPage = page,
                PageSize = pageSize,
                Data = pagedItems
            });
        }
    }
}
