using FluentValidation;
using RagApi.Api.Models;

namespace RagApi.Api.Validators;

// Argha - 2026-02-20 - Validates complex SearchRequest rules not expressible via data annotations 
// Basic rules (Query required/length, TopK range) remain on the model as data annotations.
// This validator adds: Tags list constraints.
public class SearchRequestValidator : AbstractValidator<SearchRequest>
{
    public SearchRequestValidator()
    {
        // Argha - 2026-02-20 - Tags: each item must be non-empty and ≤100 chars; list capped at 20 
        When(r => r.Tags != null, () =>
        {
            RuleFor(r => r.Tags!)
                .Must(tags => tags.Count <= 20)
                .WithMessage("Tags must not contain more than 20 items.");

            RuleForEach(r => r.Tags!)
                .NotEmpty().WithMessage("Each tag must not be empty.")
                .MaximumLength(100).WithMessage("Each tag must not exceed 100 characters.");
        });
    }
}
