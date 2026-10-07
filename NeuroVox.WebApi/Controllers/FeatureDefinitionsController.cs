using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.FeatureDefinitions;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FeatureDefinitionsController : ControllerBase
    {
        private readonly IFeatureDefinitionReadRepository _read;

        public FeatureDefinitionsController(IFeatureDefinitionReadRepository read) => _read = read;

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get FeatureDefinitions", ActionType = ActionType.Reading)]
        
        public IActionResult GetAll() => Ok(_read.GetAll(tracking: false).Where(f => !f.RowIsDeleted).ToList());
    }
}



