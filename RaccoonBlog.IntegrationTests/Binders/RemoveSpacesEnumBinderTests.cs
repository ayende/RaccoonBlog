using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RaccoonBlog.Web.Areas.Admin.Controllers;
using RaccoonBlog.Web.Helpers.Binders;
using Xunit;

namespace RaccoonBlog.IntegrationTests.Binders
{
	public class RemoveSpacesEnumBinderTests
	{
		[Theory]
		[InlineData("Mark Spam", CommentCommandOptions.MarkSpam)]
		[InlineData("Mark Ham", CommentCommandOptions.MarkHam)]
		[InlineData("Delete", CommentCommandOptions.Delete)]
		[InlineData("mark spam", CommentCommandOptions.MarkSpam)]
		[InlineData("MARKSPAM", CommentCommandOptions.MarkSpam)]
		public async System.Threading.Tasks.Task BindsValueWithSpacesToEnumMember(string posted, CommentCommandOptions expected)
		{
			var bindingContext = CreateBindingContext(posted);

			await new RemoveSpacesEnumBinder().BindModelAsync(bindingContext);

			Assert.True(bindingContext.Result.IsModelSet);
			Assert.Equal(expected, bindingContext.Result.Model);
			Assert.Empty(bindingContext.ModelState["command"]?.Errors ?? new ModelErrorCollection());
		}

		[Fact]
		public async System.Threading.Tasks.Task UnknownValueFailsBindingAndAddsModelStateError()
		{
			var bindingContext = CreateBindingContext("Garbage");

			await new RemoveSpacesEnumBinder().BindModelAsync(bindingContext);

			Assert.False(bindingContext.Result.IsModelSet);
			Assert.False(bindingContext.ModelState.IsValid);
			Assert.True(bindingContext.ModelState.ContainsKey("command"));
		}

		[Fact]
		public async System.Threading.Tasks.Task EmptyValueFallsBackToFrameworkDefault()
		{
			var bindingContext = CreateBindingContext(string.Empty);

			await new RemoveSpacesEnumBinder().BindModelAsync(bindingContext);

			Assert.False(bindingContext.Result.IsModelSet);
		}

		[Fact]
		public async System.Threading.Tasks.Task MissingValueFallsBackToFrameworkDefault()
		{
			var bindingContext = CreateBindingContext(value: null, populateValueProvider: false);

			await new RemoveSpacesEnumBinder().BindModelAsync(bindingContext);

			Assert.False(bindingContext.Result.IsModelSet);
		}

		[Fact]
		public void ProviderReturnsBinderForCommentCommandOptions()
		{
			var provider = new RemoveSpacesEnumBinderProvider();
			var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(CommentCommandOptions));
			var context = new TestModelBinderProviderContext(metadata);

			var binder = provider.GetBinder(context);

			Assert.IsType<RemoveSpacesEnumBinder>(binder);
		}

		[Fact]
		public void ProviderReturnsNullForUnrelatedTypes()
		{
			var provider = new RemoveSpacesEnumBinderProvider();
			var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(string));
			var context = new TestModelBinderProviderContext(metadata);

			var binder = provider.GetBinder(context);

			Assert.Null(binder);
		}

		private static DefaultModelBindingContext CreateBindingContext(string value, bool populateValueProvider = true)
		{
			var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(CommentCommandOptions));
			var values = populateValueProvider
				? new Dictionary<string, string> { ["command"] = value }
				: new Dictionary<string, string>();

			return new DefaultModelBindingContext
			{
				ModelMetadata = metadata,
				ModelName = "command",
				ModelState = new ModelStateDictionary(),
				ValueProvider = new TestValueProvider(values),
			};
		}

		private sealed class TestValueProvider : IValueProvider
		{
			private readonly Dictionary<string, string> _values;

			public TestValueProvider(Dictionary<string, string> values) => _values = values;

			public bool ContainsPrefix(string prefix) => _values.ContainsKey(prefix);

			public ValueProviderResult GetValue(string key) =>
				_values.TryGetValue(key, out var v)
					? new ValueProviderResult(v)
					: ValueProviderResult.None;
		}

		private sealed class TestModelBinderProviderContext : ModelBinderProviderContext
		{
			public TestModelBinderProviderContext(ModelMetadata metadata)
			{
				Metadata = metadata;
				BindingInfo = new BindingInfo();
			}

			public override BindingInfo BindingInfo { get; }
			public override ModelMetadata Metadata { get; }
			public override IModelMetadataProvider MetadataProvider { get; } = new EmptyModelMetadataProvider();

			public override IModelBinder CreateBinder(ModelMetadata metadata) =>
				throw new System.NotImplementedException();
		}
	}
}
