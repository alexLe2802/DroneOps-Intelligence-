using DroneOps.Persistence.Data;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public TestController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var canConnect = await _dbContext.Database.CanConnectAsync();

        return Ok(new
        {
            Success = canConnect
        });
    }
}