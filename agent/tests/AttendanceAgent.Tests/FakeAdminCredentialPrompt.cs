using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeAdminCredentialPrompt : IAdminCredentialPrompt
{
    public (string Email, string Password)? Result { get; set; }

    public (string Email, string Password)? PromptForCredentials() => Result;
}
