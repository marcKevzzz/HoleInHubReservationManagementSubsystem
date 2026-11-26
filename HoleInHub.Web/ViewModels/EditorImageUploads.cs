namespace HoleInHubReservationManagementSubsystem.ViewModels;

internal static class EditorImageUploads
{
    public static async Task<IReadOnlyList<byte[]>> ReadAsync(IReadOnlyList<IFormFile> files, CancellationToken ct)
    {
        if (files.Count > 8 || files.Any(file => file.Length > 4194304) || files.Sum(file => file.Length) > 20 * 1024 * 1024)
        { throw new ArgumentException("Choose up to eight images, 4 MB each and 20 MB in total."); }

        var uploads = new List<byte[]>();
        foreach (var file in files)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            uploads.Add(stream.ToArray());
        }
        return uploads;
    }
}
