using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public sealed class AiConfigurationController(AiProductMappingService aiProductMappingService) : Controller
{
    private const string StatusTempDataKey = "AiConfigurationStatusMessage";
    private const string ErrorTempDataKey = "AiConfigurationErrorMessage";

    public async Task<IActionResult> Index(int? editProviderId = null, bool createNewProvider = false)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View(await aiProductMappingService.BuildAdminViewModelAsync(
            editProviderId: editProviderId,
            createNewProvider: createNewProvider,
            statusMessage: TempData[StatusTempDataKey] as string,
            errorMessage: TempData[ErrorTempDataKey] as string,
            cancellationToken: HttpContext.RequestAborted));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProvider([Bind(Prefix = nameof(AiMappingAdminIndexViewModel.Editor))] AiProviderConfigurationInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await aiProductMappingService.BuildAdminViewModelAsync(
                editProviderId: input.Id,
                createNewProvider: !input.Id.HasValue,
                editorOverride: input,
                errorMessage: "Review the AI provider details and try again.",
                cancellationToken: HttpContext.RequestAborted));
        }

        var result = await aiProductMappingService.SaveProviderAsync(input, HttpContext.RequestAborted);
        if (!result.IsSuccess)
        {
            return View(nameof(Index), await aiProductMappingService.BuildAdminViewModelAsync(
                editProviderId: input.Id,
                createNewProvider: !input.Id.HasValue,
                editorOverride: input,
                errorMessage: result.Message,
                cancellationToken: HttpContext.RequestAborted));
        }

        TempData[StatusTempDataKey] = result.Message;
        return RedirectToAction(nameof(Index), new { editProviderId = result.ProviderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLookupEnabled(bool isEnabled)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await aiProductMappingService.SetLookupEnabledAsync(isEnabled, HttpContext.RequestAborted);
        TempData[result.IsSuccess ? StatusTempDataKey : ErrorTempDataKey] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetProviderEnabled(int id, bool isEnabled)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await aiProductMappingService.SetProviderEnabledAsync(id, isEnabled, HttpContext.RequestAborted);
        TempData[result.IsSuccess ? StatusTempDataKey : ErrorTempDataKey] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProvider(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await aiProductMappingService.DeleteProviderAsync(id, HttpContext.RequestAborted);
        TempData[result.IsSuccess ? StatusTempDataKey : ErrorTempDataKey] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
