namespace ConsoleExMediator.Application.DTOs;

/// <summary>
/// Data Transfer Object for Customer
/// Single Responsibility: Transfer customer data across boundaries
/// </summary>
public sealed record CustomerDto(
    int Id,
    string Name,
    string Email,
    DateTime MemberSince
);
