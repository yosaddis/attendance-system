using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeAdminCredentialPrompt : IAdminCredentialPrompt
{
    public (string Email, string Password)? Result { get; set; }
    public bool ThrowOnPrompt { get; set; }

    public (string Email, string Password)? PromptForCredentials()
    {
        if (ThrowOnPrompt) throw new InvalidOperationException("Simulated failure to show the admin login prompt.");
        return Result;
    }
}
