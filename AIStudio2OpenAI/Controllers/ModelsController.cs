using AIStudio2OpenAI.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIStudio2OpenAI.Controllers
{
    [ApiController]
    [Route("v1")]
    public class ModelsController : ControllerBase
    {
        private static readonly List<Model> Models = new()
        {
            new Model { Id = "gemini-2.5-pro", OwnedBy = "google" },
            new Model { Id = "gemini-2.5-flash", OwnedBy = "google" }
        };

        [HttpGet("models")]
        public IActionResult GetModels()
        {
            return Ok(new { data = Models, @object = "list" });
        }
    }
}