namespace Kanitel.Api;

using Microsoft.AspNetCore.Mvc;

public static class UploadEndpoints
{
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/gif",
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/uploads/avatar", async ([FromForm] IFormFile file, CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { error = "Avatar file is required." });
            }

            if (file.Length > MaxAvatarBytes)
            {
                return Results.BadRequest(new { error = "Avatar file must be 2 MB or smaller." });
            }

            if (!AllowedImageTypes.Contains(file.ContentType))
            {
                return Results.BadRequest(new { error = "Avatar must be a PNG, JPEG, WEBP, or GIF image." });
            }

            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);

            var dataUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(memory.ToArray())}";
            return Results.Ok(new AvatarUploadResponse(dataUrl));
        })
            .DisableAntiforgery()
            .WithTags("Uploads")
            .WithSummary("Upload avatar")
            .WithDescription("Uploads a small avatar image and returns a data URL that can be stored in a person profile or agent avatar field.");

        return app;
    }
}
