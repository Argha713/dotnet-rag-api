using FluentValidation;
using RagApi.Api.Models;

namespace RagApi.Api.Validators;

// Argha - 2026-02-20 - Validates ConversationMessage Role and Content 
// Role must be exactly "user" or "assistant" (case-insensitive).
// Content has a 10,000-char ceiling to prevent oversized context payloads.
public class ConversationMessageValidator : AbstractValidator<ConversationMessage>
{
    public ConversationMessageValidator()
    {
        RuleFor(m => m.Role)
            .NotEmpty().WithMessage("Role must not be empty.")
            .Must(role => role.Equals("user", StringComparison.OrdinalIgnoreCase)
                       || role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Role must be 'user' or 'assistant'.");

        RuleFor(m => m.Content)
            .NotEmpty().WithMessage("Content must not be empty.")
            .MaximumLength(10000).WithMessage("Content must not exceed 10,000 characters.");
    }
}
