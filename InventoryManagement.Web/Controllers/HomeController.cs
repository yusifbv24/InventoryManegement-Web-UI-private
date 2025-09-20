using System.Diagnostics;
using InventoryManagement.Web.Models;
using InventoryManagement.Web.Models.ViewModels;
using InventoryManagement.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventoryManagement.Web.Controllers
{
    [Authorize]
    public class HomeController : BaseController
    {
        private readonly IApiService _apiService;
        public HomeController(IApiService apiService,ILogger<HomeController> logger)
            :base(logger)
        {
            _apiService = apiService;
        }

        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated ?? false)
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }


        public async Task<IActionResult> Dashboard(string period = "week")
        {
            var model = new DashboardViewModel();

            try
            {
                // Calculate date range based on period
                var now = DateTime.Now;
                DateTime startDate;
                DateTime endDate = now;

                switch (period.ToLower())
                {
                    case "week":
                        startDate = now.AddDays(-7);
                        break;
                    case "month":
                        startDate = now.AddMonths(-1);
                        break;
                    case "6months":
                        startDate = now.AddMonths(-6);
                        break;
                    case "all":
                        startDate = new DateTime(2020, 1, 1);
                        break;
                    default:
                        startDate = now.AddDays(-7);
                        period = "week";
                        break;
                }

                // Get routes for the specific period
                var routesTask = _apiService.GetAsync<PagedResultDto<RouteViewModel>>(
                    $"api/inventoryroutes?pageSize=10000&pageNumber=1&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");

                // Get ALL products (we'll filter them appropriately)
                var allProductsTask = _apiService.GetAsync<PagedResultDto<ProductViewModel>>(
                    "api/products?pageSize=10000&pageNumber=1");

                // Get departments and categories
                var departmentsTask = _apiService.GetAsync<List<DepartmentViewModel>>("/api/departments");
                var categoriesTask = _apiService.GetAsync<PagedResultDto<CategoryViewModel>>("/api/categories/paged?pageSize=1000");

                await Task.WhenAll(routesTask, allProductsTask, departmentsTask, categoriesTask);

                var allProducts = await allProductsTask;
                var routes = await routesTask;
                var departments = await departmentsTask;
                var categoriesResult = await categoriesTask;

                // Process products based on period
                if (allProducts != null)
                {
                    var products = allProducts.Items;

                    if (period.ToLower() == "all")
                    {
                        // For "all" period, show total products and active products
                        model.TotalProducts = products.Count();
                        model.ActiveProducts = products.Count(p => p.IsActive);
                    }
                    else
                    {
                        // For specific periods, count products created in that period
                        var productsInPeriod = products.Where(p =>
                            p.CreatedAt >= startDate &&
                            p.CreatedAt <= endDate).ToList();

                        model.TotalProducts = productsInPeriod.Count();
                        model.ActiveProducts = productsInPeriod.Count(p => p.IsActive);
                    }
                }

                // Process routes for the period
                if (routes != null)
                {
                    var transferRoutes = routes.Items.Where(r => r.RouteTypeName == "Transfer").ToList();
                    model.TotalRoutes = transferRoutes.Count();
                    model.CompletedTransfers = transferRoutes.Count(r => r.IsCompleted);
                    model.PendingTransfers = transferRoutes.Count(r => !r.IsCompleted);
                }

                // Process departments (period-aware)
                if (departments != null && routes != null)
                {
                    var activeDepartments = departments.Where(d => d.IsActive).ToList();

                    model.DepartmentStats = activeDepartments.Select(d =>
                    {
                        // Get all transfer routes TO this department in the period
                        var transfersToThisDept =routes.Items
                            .Where(r=>r.ToDepartmentId==d.Id &&
                                      r.RouteTypeName=="Transfer" &&
                                      r.CreatedAt>=startDate &&
                                      r.CreatedAt<=endDate)
                            .ToList();

                        // Count completed transfers (products received)
                        var completedTransfers = transfersToThisDept
                            .Where(r=> r.IsCompleted)
                            .Count();

                        // Get unique workers who received products in this department
                        // This counts unique worker names from the transfers TO this department
                        var uniqueWorkers = transfersToThisDept
                            .Where(r => !string.IsNullOrEmpty(r.ToWorker))
                            .Select(r => r.ToWorker!.Trim().ToLower()) // Normalize names for comparison
                            .Distinct()
                            .Count();

                        return new DepartmentStats
                        {
                            DepartmentName = d.Name,
                            ProductCount = completedTransfers,
                            ActiveWorkers = uniqueWorkers,
                            PeriodTransfers=transfersToThisDept.Count()
                        };
                    })
                    .Where(d=>d.ProductCount > 0 || d.PeriodTransfers > 0) // Only show departments with activity in the period
                    .OrderByDescending(d => d.PeriodTransfers)
                    .Take(5)
                    .ToList();
                }

                // Process categories (period-aware)
                if (categoriesResult != null && allProducts != null)
                {
                    var categories = categoriesResult.Items.Where(c => c.IsActive).ToList();
                    var colors = new[] {
                        "#3B82F6", // Blue
                        "#10B981", // Green
                        "#F59E0B", // Yellow
                        "#EF4444", // Red
                        "#8B5CF6", // Purple
                        "#EC4899", // Pink
                        "#06B6D4", // Cyan
                        "#14B8A6"  // Teal 
                    };

                    model.CategoryDistributions = categories.Select((c, index) =>
                    {
                        // Count products in this category based on period
                        var categoryProductCount = period.ToLower() == "all"
                            ? allProducts.Items.Count(p => p.CategoryId == c.Id)
                            : allProducts.Items.Count(p => p.CategoryId == c.Id && 
                                p.IsNewItem && 
                                p.CreatedAt >= startDate && 
                                p.CreatedAt <= endDate);

                        return new CategoryDistribution
                        {
                            CategoryName = c.Name,
                            Count = categoryProductCount,
                            Color = colors[index % colors.Length]
                        };
                    })
                    .Where(c => c.Count > 0)
                    .OrderByDescending(c => c.Count)
                    .Take(8)
                    .ToList();
                }

                // Generate transfer activity data based on actual routes
                model.TransferActivityData = GenerateTransferActivityData(routes?.Items?.ToList() ?? new List<RouteViewModel>(), period, startDate, endDate);

                // Set ViewBag data
                ViewBag.CurrentPeriod = period;
                ViewBag.PeriodStartDate = startDate;
                ViewBag.PeriodEndDate = endDate;
                ViewBag.TotalDepartments = departments?.Count(d => d.IsActive) ?? 0;
                ViewBag.TotalCategories = categoriesResult?.Items?.Count(c => c.IsActive) ?? 0;

                return View(model);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading dashboard");
                return HandleException(ex, new DashboardViewModel());
            }
        }


        /// <summary>
        /// Generate transfer activity data based on actual route data
        /// </summary>
        private TransferActivityData GenerateTransferActivityData(IEnumerable<RouteViewModel> routes, string period, DateTime startDate, DateTime endDate)
        {
            var labels = new List<string>();
            var completedData = new List<int>();
            var pendingData = new List<int>();

            switch (period.ToLower())
            {
                case "week":
                    // Group by days for the past week
                    for (int i = 6; i >= 0; i--)
                    {
                        var date = DateTime.Now.AddDays(-i);
                        labels.Add(date.ToString("ddd"));
                        
                        var dayRoutes = routes.Where(r => r.CreatedAt.Date == date.Date).ToList();
                        completedData.Add(dayRoutes.Count(r => r.IsCompleted));
                        pendingData.Add(dayRoutes.Count(r => !r.IsCompleted));
                    }
                    break;

                case "month":
                    // Group by weeks for the past month
                    var weekStart = startDate;
                    var weekNumber = 1;
                    
                    while (weekStart < endDate)
                    {
                        var weekEnd = weekStart.AddDays(7);
                        if (weekEnd > endDate) weekEnd = endDate;

                        labels.Add($"Week {weekNumber}");
                        
                        var weekRoutes = routes.Where(r => r.CreatedAt >= weekStart && r.CreatedAt < weekEnd).ToList();
                        completedData.Add(weekRoutes.Count(r => r.IsCompleted));
                        pendingData.Add(weekRoutes.Count(r => !r.IsCompleted));

                        weekStart = weekEnd;
                        weekNumber++;
                    }
                    break;

                case "6months":
                    // Group by months for the past 6 months
                    for (int i = 5; i >= 0; i--)
                    {
                        var monthStart = DateTime.Now.AddMonths(-i).Date;
                        var monthEnd = monthStart.AddMonths(1);
                        
                        labels.Add(monthStart.ToString("MMM"));
                        
                        var monthRoutes = routes.Where(r => r.CreatedAt >= monthStart && r.CreatedAt < monthEnd).ToList();
                        completedData.Add(monthRoutes.Count(r => r.IsCompleted));
                        pendingData.Add(monthRoutes.Count(r => !r.IsCompleted));
                    }
                    break;

                case "all":
                    // Group by quarters for all time
                    var allRoutes = routes.OrderBy(r => r.CreatedAt).ToList();
                    if (allRoutes.Any())
                    {
                        var firstDate = allRoutes.First().CreatedAt;
                        var quarterStart = new DateTime(firstDate.Year, ((firstDate.Month - 1) / 3) * 3 + 1, 1);
                        
                        while (quarterStart < endDate)
                        {
                            var quarterEnd = quarterStart.AddMonths(3);
                            labels.Add($"Q{((quarterStart.Month - 1) / 3) + 1} {quarterStart.Year}");
                            
                            var quarterRoutes = routes.Where(r => r.CreatedAt >= quarterStart && r.CreatedAt < quarterEnd).ToList();
                            completedData.Add(quarterRoutes.Count(r => r.IsCompleted));
                            pendingData.Add(quarterRoutes.Count(r => !r.IsCompleted));
                            
                            quarterStart = quarterEnd;
                        }
                    }
                    break;
            }

            return new TransferActivityData
            {
                Labels = labels,
                CompletedData = completedData,
                PendingData = pendingData
            };
        }


        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var errorViewModel = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };

            // Check if this is an AJAX request
            if (IsAjaxRequest())
            {
                return Json(new
                {
                    isSuccess = false,
                    message = "An error occurred while processing your request",
                    requestId = errorViewModel.RequestId
                });
            }

            return View(errorViewModel);
        }
    }
}