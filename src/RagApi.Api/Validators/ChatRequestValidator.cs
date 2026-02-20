using FluentValidation;
using RagApi.Api.Models;

namespace RagApi.Api.Validators;

// Argha - 2026-02-20 - Validates complex ChatRequest rules not expressible via data annotations (Phase 4.2)
// Basic rules (Query required/length, TopK range) remain on the model as data annotations.
// This validator adds: Tags list constraints and ConversationHistory message validation.
public class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        // Argha - 2026-02-20 - Tags: each item must be non-empty and ≤100 chars; list capped at 20 (Phase 4.2)
        When(r => r.Tags != null, () =>
        {
            RuleFor(r => r.Tags!)
                .Must(tags => tags.Count <= 20)
                .WithMessage("Tags must not contain more than 20 items.");

            RuleForEach(r => r.Tags!)
                .NotEmpty().WithMessage("Each tag must not be empty.")
                .MaximumLength(100).WithMessage("Each tag must not exceed 100 characters.");
        });

        // Argha - 2026-02-20 - ConversationHistory: delegate each message to ConversationMessageValidator (Phase 4.2)
        When(r => r.ConversationHistory != null, () =>
        {
            RuleForEach(r => r.ConversationHistory!)
                .SetValidator(new ConversationMessageValidator());
        });
    }
}
