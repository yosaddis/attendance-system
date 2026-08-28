namespace AttendanceAgent.Services;

public class InputBoxAdminCredentialPrompt : IAdminCredentialPrompt
{
    public (string Email, string Password)? PromptForCredentials()
    {
        var email = Microsoft.VisualBasic.Interaction.InputBox("Admin email:", "Admin login");
        if (string.IsNullOrWhiteSpace(email)) return null;

        var password = Microsoft.VisualBasic.Interaction.InputBox("Admin password:", "Admin login");
        if (string.IsNullOrWhiteSpace(password)) return null;

        return (email, password);
    }
}
