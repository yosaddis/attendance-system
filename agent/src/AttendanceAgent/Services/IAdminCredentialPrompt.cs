namespace AttendanceAgent.Services;

/// <summary>
/// Exists so MainViewModel never calls a real blocking modal dialog directly — a unit test
/// invoking a real Microsoft.VisualBasic.Interaction.InputBox call would hang waiting for actual
/// user input. Returns null if the operator leaves either field blank (treated as "cancelled").
/// </summary>
public interface IAdminCredentialPrompt
{
    (string Email, string Password)? PromptForCredentials();
}
