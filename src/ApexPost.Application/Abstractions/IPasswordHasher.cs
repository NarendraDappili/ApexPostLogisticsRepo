namespace ApexPost.Application.Abstractions;

/// <summary>Creates and verifies salted, slow password hashes. Plain-text passwords are never stored.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string? passwordHash);
}
