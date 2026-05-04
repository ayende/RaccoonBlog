using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RaccoonBlog.Web.Areas.Admin.Controllers;

namespace RaccoonBlog.Web.Helpers.Binders
{
	/// <summary>
	/// Strips spaces from a posted value before binding it to an enum, so that
	/// human-readable form values like "Mark Spam" map to enum members like MarkSpam.
	/// </summary>
	public class RemoveSpacesEnumBinder : IModelBinder
	{
		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if (bindingContext == null)
			{
				throw new ArgumentNullException(nameof(bindingContext));
			}

			var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

			if (valueProviderResult == ValueProviderResult.None)
			{
				return Task.CompletedTask;
			}

			bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

			var rawValue = valueProviderResult.FirstValue;
			if (string.IsNullOrEmpty(rawValue))
			{
				return Task.CompletedTask;
			}

			var stripped = rawValue.Replace(" ", string.Empty);
			var enumType = Nullable.GetUnderlyingType(bindingContext.ModelType) ?? bindingContext.ModelType;

			if (Enum.TryParse(enumType, stripped, ignoreCase: true, out var parsed)
				&& Enum.IsDefined(enumType, parsed))
			{
				bindingContext.Result = ModelBindingResult.Success(parsed);
			}
			else
			{
				bindingContext.ModelState.TryAddModelError(
					bindingContext.ModelName,
					$"The value '{rawValue}' is not valid for {bindingContext.ModelName}.");
			}

			return Task.CompletedTask;
		}
	}

	public class RemoveSpacesEnumBinderProvider : IModelBinderProvider
	{
		public IModelBinder GetBinder(ModelBinderProviderContext context)
		{
			if (context == null)
			{
				throw new ArgumentNullException(nameof(context));
			}

			var modelType = context.Metadata.ModelType;
			var underlying = Nullable.GetUnderlyingType(modelType) ?? modelType;

			if (underlying == typeof(CommentCommandOptions))
			{
				return new RemoveSpacesEnumBinder();
			}

			return null;
		}
	}
}
