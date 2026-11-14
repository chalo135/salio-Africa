using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Salio.Api.Controllers;

// Stops ASP.NET Core reading a form before the action runs. Without this, a
// file over the size limit is turned into a "One or more validation errors
// occurred" answer before the action can say anything plain. With it, the
// action reads the form itself (Request.ReadFormAsync) and words the refusal.
// Used on the supplier-invoice upload only.
[AttributeUsage(AttributeTargets.Method)]
public class ReadFormByHandAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;

        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
