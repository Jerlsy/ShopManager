using System.Text.Json.Serialization;

namespace ShopManager.Models;

internal record CalendarDayDto(
    [property: JsonPropertyName("date")]        string Date,
    [property: JsonPropertyName("week")]        string Week,
    [property: JsonPropertyName("isHoliday")]   bool   IsHoliday,
    [property: JsonPropertyName("description")] string Description);
