using System.Collections.Generic;
using System.Text.Json;

namespace Local_Network_Messenger.Models
{
    public sealed record WebMessage(string Id, string Type, JsonElement Payload);

    public sealed record WebResponse(string Id, string Type, bool Ok, object? Payload, IReadOnlyList<ValidationError>? Errors);
}
