namespace ConsoleExMediator.Domain.Entities;

/// <summary>
/// Domain entity representing a customer
/// Single Responsibility: Manages customer data and behavior
/// </summary>
public sealed class Customer
{
    public int Id { get; private set; }
    public string Name { get; private set; }
    public string Email { get; private set; }
    public DateTime MemberSince { get; private set; }

    // Private constructor for entity framework or serialization
    private Customer() 
    {
        Name = string.Empty;
        Email = string.Empty;
    }

    public Customer(int id, string name, string email, DateTime memberSince)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name is required", nameof(name));
        
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("Valid email is required", nameof(email));

        Id = id;
        Name = name;
        Email = email;
        MemberSince = memberSince;
    }

    public void UpdateEmail(string newEmail)
    {
        if (string.IsNullOrWhiteSpace(newEmail) || !newEmail.Contains('@'))
            throw new ArgumentException("Valid email is required", nameof(newEmail));
        
        Email = newEmail;
    }
}
