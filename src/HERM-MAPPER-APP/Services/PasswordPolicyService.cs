namespace HERM_MAPPER_APP.Services;

public sealed class PasswordPolicyService
{
    public PasswordPolicyValidationResult Validate(string? password)
    {
        var errors = new List<string>();
        var candidate = password ?? string.Empty;

        if (candidate.Length < 12)
        {
            errors.Add("Password must be at least 12 characters long.");
        }

        if (!candidate.Any(char.IsUpper))
        {
            errors.Add("Password must include an uppercase letter.");
        }

        if (!candidate.Any(char.IsLower))
        {
            errors.Add("Password must include a lowercase letter.");
        }

        if (!candidate.Any(char.IsDigit))
        {
            errors.Add("Password must include a number.");
        }

        if (!candidate.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            errors.Add("Password must include a special character.");
        }

        return new PasswordPolicyValidationResult(errors.Count == 0, errors, CalculateStrength(candidate));
    }

    public int CalculateStrength(string? password)
    {
        var candidate = password ?? string.Empty;
        var score = 0;

        if (candidate.Length >= 12)
        {
            score += 35;
        }

        if (candidate.Length >= 16)
        {
            score += 10;
        }

        if (candidate.Any(char.IsLower))
        {
            score += 15;
        }

        if (candidate.Any(char.IsUpper))
        {
            score += 15;
        }

        if (candidate.Any(char.IsDigit))
        {
            score += 15;
        }

        if (candidate.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            score += 10;
        }

        return Math.Clamp(score, 0, 100);
    }
}

public sealed record PasswordPolicyValidationResult(bool IsValid, IReadOnlyList<string> Errors, int StrengthScore);