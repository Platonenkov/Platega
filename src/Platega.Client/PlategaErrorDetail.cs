namespace Platega;

/// <summary>One entry of the <c>data</c> array in a Platega error body.</summary>
/// <param name="Key">Field or parameter the message refers to, e.g. <c>Id</c>.</param>
/// <param name="Message">Explanation from Platega.</param>
public sealed record PlategaErrorDetail(string? Key, string? Message);
