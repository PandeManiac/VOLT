using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class NumericFieldTests
{
	[Fact]
	public void SetValue_ChangedValue_UpdatesText()
	{
		using UiTestHost host = new();
		NumericField field = host.Add(new NumericField());

		field.SetValue(2.5f, notify: false);

		Assert.Equal(2.5f, field.Value, 3);
		Assert.Equal("2.5", field.Text);

		field.SetValue(-4f, notify: false);

		Assert.Equal("-4", field.Text);
	}

	[Fact]
	public void SetValue_RepeatedUnchangedValue_LeavesTextAlone()
	{
		using UiTestHost host = new();
		NumericField field = host.Add(
			new NumericField
			{
				Format = "0.00"
			});

		field.SetValue(1.25f, notify: false);

		Assert.Equal("1.25", field.Text);

		int textChanged = 0;

		field.TextChanged += _ => textChanged++;

		for (int index = 0; index < 64; index++)
		{
			field.SetValue(1.25f, notify: false);
		}

		Assert.Equal("1.25", field.Text);
		Assert.Equal(0, textChanged);
	}

	[Fact]
	public void SetValue_RejectedEdit_RevertsTextToCurrentValue()
	{
		using UiTestHost host = new();
		NumericField field = host.Add(new NumericField());

		field.SetValue(3f, notify: false);
		field.Text = "not a number";

		field.SetValue(3f, notify: false);

		Assert.Equal(3f, field.Value, 3);
		Assert.Equal("3", field.Text);
	}

	[Fact]
	public void SetValue_AfterFormatChange_ReformatsText()
	{
		using UiTestHost host = new();
		NumericField field = host.Add(new NumericField());

		field.SetValue(1.5f, notify: false);

		Assert.Equal("1.5", field.Text);

		field.Format = "0.000";
		field.SetValue(1.5f, notify: false);

		Assert.Equal("1.500", field.Text);
	}

	[Fact]
	public void SetValue_ClampsAndReportsValueChangedOnce()
	{
		using UiTestHost host = new();
		NumericField field = host.Add(
			new NumericField
			{
				Minimum = 0f,
				Maximum = 10f
			});

		int changes = 0;

		field.ValueChanged += _ => changes++;

		field.Value = 42f;

		Assert.Equal(10f, field.Value, 3);
		Assert.Equal("10", field.Text);
		Assert.Equal(1, changes);

		field.Value = 42f;

		Assert.Equal(1, changes);
	}
}
