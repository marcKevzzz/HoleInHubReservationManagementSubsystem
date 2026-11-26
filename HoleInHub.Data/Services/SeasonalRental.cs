namespace HoleInHub.Data.Services;

public static class SeasonalRental
{
    public const string Whole = "ChristmasWholeShop";
    public const string Regular = "ChristmasRegularBays";
    public const string Vip = "ChristmasVipBays";
    public static bool IsPackage(string? id) => id is Whole or Regular or Vip;
    public static bool Includes(string? id, string service) => id switch
    {
        Regular => service == "Regular",
        Vip => service == "VIP",
        _ => true
    };
}
