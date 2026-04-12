using Microsoft.AspNetCore.Mvc;
using WsLink.Common.Contract;
using WsLink.Common.Service;

namespace WsLink.Api.Controllers;

[ApiController]
[Route("data")]
public class WsLinkDataController(IWeatherService weatherService, ILogger<WsLinkDataController> logger): ControllerBase
{

    [HttpGet("upload.php")]
    [HttpPost("upload.php")]
    public async Task Get([FromQuery] WsLinkRequestParam requestParam)
    {
        logger.LogDebug("Data received [Data: {@Data}]", requestParam);
        await weatherService.SaveWeatherData(requestParam);
    }
}