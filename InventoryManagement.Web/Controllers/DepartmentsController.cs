﻿using InventoryManagement.Web.Models.DTOs;
using InventoryManagement.Web.Models.ViewModels;
using InventoryManagement.Web.Services;
using InventoryManagement.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventoryManagement.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class DepartmentsController : BaseController
    {
        private readonly IApiService _apiService;
        private readonly IUrlService _urlService;
        private readonly IWordExportService _wordExportService;

        public DepartmentsController(
            IApiService apiService, 
            ILogger<DepartmentsController> logger,
            IUrlService urlService,
            IWordExportService wordExportService)
            : base(logger)
        {
            _apiService = apiService;
            _urlService = urlService;
            _wordExportService = wordExportService;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1, 
            int pageSize = 20, 
            string? search = null)
        {
            try
            {
                var queryString = $"?pageNumber={pageNumber}&pageSize={pageSize}";
                if (!string.IsNullOrWhiteSpace(search))
                    queryString += $"&search={Uri.EscapeDataString(search)}";

                var result = await _apiService.GetAsync<PagedResultDto<DepartmentViewModel>>(
                    $"api/departments/paged{queryString}");

                var allDepartments=await _apiService.GetAsync<List<DepartmentViewModel>>(
                    $"api/departments");

                if (result == null)
                {
                    result = new PagedResultDto<DepartmentViewModel>
                    {
                        Items = [],
                        TotalCount = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                if (allDepartments != null)
                {
                    var activeDepartments = 0;
                    var inActiveDepartments = 0;
                    var departmentsInWithProducts = 0;

                    if (!string.IsNullOrEmpty(search))
                    {
                        activeDepartments = result.Items.Count(c => c.IsActive);
                        inActiveDepartments = result.Items.Count(c => !c.IsActive);
                        departmentsInWithProducts = result.Items.Sum(c => c.ProductCount);
                    }
                    else
                    {
                        activeDepartments = allDepartments.Count(c => c.IsActive);
                        inActiveDepartments = allDepartments.Count(c => !c.IsActive);
                        departmentsInWithProducts = allDepartments.Sum(c => c.ProductCount);
                    }
                    
                    ViewBag.ActiveDepartments = activeDepartments;
                    ViewBag.InActiveDepartments = inActiveDepartments;
                    ViewBag.DepartmentsInWithProducts = departmentsInWithProducts;
                }

                ViewBag.CurrentSearch = search;
                ViewBag.PageNumber = pageNumber;
                ViewBag.PageSize = pageSize;

                return View(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex, new PagedResultDto<DepartmentViewModel>
                {
                    Items = new List<DepartmentViewModel>(),
                    TotalCount = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                });
            }
        }



        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var department = await _apiService.GetAsync<DepartmentViewModel>($"api/departments/{id}");
                if (department == null)
                    return NotFound();

                // Get products for this department
                var products = await _apiService.GetAsync<PagedResultDto<ProductViewModel>>(
                    $"api/products?pageSize=10000&pageNumber=1&departmentId={id}");

                var departmentProducts = products?.Items?? new List<ProductViewModel>();

                // Update the image URLs for display
                foreach (var product in departmentProducts)
                {
                    if (!string.IsNullOrEmpty(product.ImageUrl))
                    {
                        product.FullImageUrl = _urlService.GetImageUrl(product.ImageUrl);
                    }
                }

                ViewBag.Products = departmentProducts.ToList();

                // Update counts
                department.ProductCount = departmentProducts.Count();
                department.WorkerCount = departmentProducts
                    .Where(w => !string.IsNullOrEmpty(w.Worker))
                    .Select(w => w.Worker)
                    .Distinct()
                    .Count();

                return View(department);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading department details for ID: {DepartmentId}", id);
                return HandleException(ex);
            }
        }



        public IActionResult Create()
        {
            return View(new DepartmentViewModel());
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DepartmentViewModel model)
        {
            if(!ModelState.IsValid)
            {
                return HandleValidationErrors(ModelState);
            }
            try
            {
                var dto = new CreateDepartmentDto
                {
                    Name = model.Name,
                    Description = model.Description,
                    IsActive = model.IsActive
                };

                var response = await _apiService.PostAsync<CreateDepartmentDto, DepartmentDto>("api/departments", dto);
                return HandleApiResponse(response, "Index");
            }
            catch(Exception ex)
            {
                return HandleException(ex, model);
            }
        }



        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var department = await _apiService.GetAsync<DepartmentViewModel>($"api/departments/{id}");
                if (department == null)
                    return NotFound();

                return View(department);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DepartmentViewModel model)
        {
            if(!ModelState.IsValid)
            {
                return HandleValidationErrors(ModelState);
            }
            try
            {
                var dto= new UpdateDepartmentDto
                {
                    Name = model.Name,
                    Description = model.Description,
                    IsActive = model.IsActive
                };
                var response = await _apiService.PutAsync<UpdateDepartmentDto, bool>($"api/departments/{id}", dto);
                return HandleApiResponse(response, "Index");
            }
            catch (Exception ex)
            {
                return HandleException(ex, model);
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var response = await _apiService.DeleteAsync($"api/departments/{id}");
                return HandleApiResponse(response, "Index");
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }



        [HttpGet("{id}/export-word")]
        public async Task<IActionResult> ExportToWord(int id)
        {
            try
            {
                // Get department details
                var department = await _apiService.GetAsync<DepartmentViewModel>($"api/departments/{id}");
                if (department == null)
                    return NotFound();

                // Get products for this department
                var products=await _apiService.GetAsync<PagedResultDto<ProductViewModel>>(
                                $"api/products?pageSize=10000&pageNumber=1&departmentId={id}");

                var departmentProducts=products?.Items?.ToList() ?? new List<ProductViewModel>();

                // Update the image URLs for display (though we won't include images in Word)
                foreach(var product in departmentProducts)
                {
                    if (!string.IsNullOrEmpty(product.ImageUrl))
                    {
                        product.FullImageUrl=_urlService.GetImageUrl(product.ImageUrl);
                    }
                }

                // Generate Word document
                var fileBytes = _wordExportService.GenerateDepartmentInventoryDocument(department, departmentProducts);

                // Return as downloadable file
                var fileName = $"{department.Name}_Inventory_{DateTime.Now:yyyyMMdd}.docx";

                return File(fileBytes,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    fileName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error exporting department {DepartmentId} to Word", id);
                return HandleException(ex);
            }
        }
    }
}