using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class ProductController : Controller
    {
        private readonly TableService _tableService;
        private readonly BlobService _blobService;

        public ProductController(TableService tableService, BlobService blobService)
        {
            _tableService = tableService;
            _blobService = blobService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var products = await _tableService.GetAllProductsAsync();
                return View(products);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Unable to load products. Please try again.";
                return View(new List<Product>());
            }
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Product product, IFormFile imageFile)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        product.ImageUrl = await _blobService.UploadImageAsync(imageFile);
                    }

                    await _tableService.InsertProductAsync(product);
                    TempData["Success"] = "Product added successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Unable to save product. Please try again.");
                }
            }
            return View(product);
        }
    }
}