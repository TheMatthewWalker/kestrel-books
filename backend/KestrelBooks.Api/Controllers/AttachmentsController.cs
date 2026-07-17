using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/attachments")]
public class AttachmentsController : ControllerBase
{
    private readonly AccessService _access;
    private readonly AttachmentService _attachments;
    public AttachmentsController(AccessService access, AttachmentService attachments)
    {
        _access = access; _attachments = attachments;
    }

    [HttpPost]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> Upload(Guid businessId, IFormFile file,
        [FromForm] AttachedTo entityKind, [FromForm] Guid entityId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file received." });
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var attachment = await _attachments.SaveAsync(businessId, entityKind, entityId,
            file.FileName, file.ContentType, ms.ToArray(), AccessService.UserId(User));
        return Ok(new { attachment.Id, attachment.FileName, attachment.SizeBytes, attachment.UploadedAtUtc });
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId, [FromQuery] AttachedTo entityKind, [FromQuery] Guid entityId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var items = await _attachments.ListAsync(businessId, entityKind, entityId);
        return Ok(items.Select(a => new { a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAtUtc }));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var result = await _attachments.GetAsync(businessId, id);
        if (result is null) return NotFound();
        return File(result.Value.data, result.Value.meta.ContentType, result.Value.meta.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        return await _attachments.DeleteAsync(businessId, id) ? NoContent() : NotFound();
    }
}
