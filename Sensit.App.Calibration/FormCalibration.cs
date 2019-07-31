﻿using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sensit.TestSDK.Forms;
using Sensit.TestSDK.Interfaces;
using Sensit.TestSDK.Settings;
using Sensit.TestSDK.Utilities;

namespace Sensit.App.Calibration
{
	/// <summary>
	/// GUI operations and settings access are handled here.
	/// </summary>
	public partial class FormCalibration : Form
	{
		#region Constants

		// These constants specify the order that controls appear in the 
		// columns of tableLayoutPanelDevicesUnderTest.
		private const int DUT_COLUMN_CHECKBOX = 0;
		private const int DUT_COLUMN_SERIALNUM = 1;
		private const int DUT_COLUMN_MODEL = 2;
		private const int DUT_COLUMN_STATUS = 3;

		#endregion

		#region Fields

		// allow the form to wait for tests to cancel/complete before closing application
		private bool _closeAfterTest = false;

		// Object to represent test equipment.
		private Equipment _equipment;

		// Object to represent devices under test.
		private List<Dut> _duts = new List<Dut>();

		// Object to represent tests.
		private Test _test;

		#endregion

		#region Properties

		/// <summary>
		/// Number of DUTs displayed on the form
		/// </summary>
		public int NumDuts
		{
			get => Properties.Settings.Default.NumDuts;
			set
			{
				Properties.Settings.Default.NumDuts = value;

				// Stop the GUI from looking weird while we update it.
				tableLayoutPanelDevicesUnderTest.SuspendLayout();

				// Remove all DUT controls.
				for (int i = 0; i < tableLayoutPanelDevicesUnderTest.ColumnCount; i++)
				{
					for (int j = 0; j < tableLayoutPanelDevicesUnderTest.RowCount; j++)
					{
						Control control = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(i, j);
						tableLayoutPanelDevicesUnderTest.Controls.Remove(control);
					}
				}

				// Set how many rows there should be.
				tableLayoutPanelDevicesUnderTest.RowCount = value;

				// Create new DUT controls.
				for (int i = 1; i <= value; i++)
				{
					CheckBox checkBox = new CheckBox
					{
						Name = "checkBoxSelected" + i.ToString(),
						AutoSize = true,
						Anchor = AnchorStyles.Left | AnchorStyles.Top,
						Dock = DockStyle.None
					};
					tableLayoutPanelDevicesUnderTest.Controls.Add(checkBox, DUT_COLUMN_CHECKBOX, i - 1);

					TextBox textBoxSerialNumber = new TextBox
					{
						Name = "textBoxSerialNumber" + i.ToString(),
						Anchor = AnchorStyles.Left | AnchorStyles.Top,
						Dock = DockStyle.None,
						Text = "DUT" + i.ToString()
					};
					tableLayoutPanelDevicesUnderTest.Controls.Add(textBoxSerialNumber, DUT_COLUMN_SERIALNUM, i - 1);

					ComboBox comboBoxModel = new ComboBox
					{
						Name = "comboBoxModel" + i.ToString(),
						Anchor = AnchorStyles.Left | AnchorStyles.Top,
						Dock = DockStyle.None,
						DropDownStyle = ComboBoxStyle.DropDownList
					};
					tableLayoutPanelDevicesUnderTest.Controls.Add(comboBoxModel, DUT_COLUMN_MODEL, i - 1);

					Label labelStatus = new Label
					{
						Name = "labelStatus" + i.ToString(),
						AutoSize = true,
						Anchor = AnchorStyles.Left | AnchorStyles.Top,
						Dock = DockStyle.None
					};
					tableLayoutPanelDevicesUnderTest.Controls.Add(labelStatus, DUT_COLUMN_STATUS, i - 1);
				}

				// Make the GUI act normally again.
				tableLayoutPanelDevicesUnderTest.ResumeLayout();
			}
		}

		#endregion

		#region Constructor

		public FormCalibration()
		{
			// Initialize the form.
			InitializeComponent();

			// Add version string to title bar.
			if (ApplicationDeployment.IsNetworkDeployed)
			{
				Text += " " + ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
			}

			InitEquipmentMenu(typeof(IControlDevice));
			InitEquipmentMenu(typeof(IReferenceDevice));

			// Set the number of DUTs.
			NumDuts = Properties.Settings.Default.NumDuts;

			// Select the most recently used DUTs.
			if (Properties.Settings.Default.DutSelections != null)
			{
				List<string> list = Properties.Settings.Default.DutSelections.Cast<string>().ToList();
				bool[] selections = list.Select(x => x == "true").ToArray();
				for (int i = 0; i < NumDuts; i++)
				{
					CheckBox checkBox = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_CHECKBOX, i) as CheckBox;
					checkBox.Checked = selections[i];
				}
			}

			// Populate the Model combobox (at the bottom of the form, which updates all the individual controls) based on DUT settings.
			comboBoxModel.Items.Clear();
			DutSettings dutSettings = Settings.Load<DutSettings>(Properties.Settings.Default.DutSettingsFile);
			foreach (ModelSetting model in dutSettings.ModelSettings ?? new List<ModelSetting>())
			{
				comboBoxModel.Items.Add(model.Label);

				// Do the same for the Model selection combobox for each DUT.
				for (int i = 0; i < NumDuts; i++)
				{
					ComboBox comboBox = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_MODEL, i) as ComboBox;
					comboBox.Items.Add(model.Label);
				}
			}

			// Select the most recently used model, or the first if that's not available.
			// This has to be done before setting the individual model selections, or it will override them.
			int index = comboBoxModel.FindStringExact(Properties.Settings.Default.Model);
			comboBoxModel.SelectedIndex = index == -1 ? 0 : index;

			// Select the individual Models.
			// This must be done after selecting the "Set all models" comboBox, or it will override the individual settings.
			if (Properties.Settings.Default.ModelSelections != null)
			{
				List<string> list = Properties.Settings.Default.ModelSelections.Cast<string>().ToList();
				for (int i = 0; i < NumDuts; i++)
				{
					ComboBox comboBox = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_MODEL, i) as ComboBox;
					int j = comboBox.FindStringExact(list[i]);
					comboBox.SelectedIndex = j == -1 ? 0 : j;
				}
			}

			// Populate the Range combobox based on the DUT settings.
			comboBoxRange.Items.Clear();
			foreach (RangeSetting r in dutSettings.RangeSettings ?? new List<RangeSetting>())
			{
				comboBoxRange.Items.Add(r.Label);
			}

			// Select the most recently used range, or the first if that's not available.
			index = comboBoxRange.FindStringExact(Properties.Settings.Default.Range);
			comboBoxRange.SelectedIndex = index == -1 ? 0 : index;

			// Populate the Test combobox based on the test settings.
			comboBoxTest.Items.Clear();
			TestSettings testSettings = Settings.Load<TestSettings>(Properties.Settings.Default.TestSettingsFile);
			foreach (TestSetting t in testSettings.Tests ?? new List<TestSetting>())
			{
				comboBoxTest.Items.Add(t.Label);
			}

			// Select the most recently used test, or the first if that's not available.
			index = comboBoxTest.FindStringExact(Properties.Settings.Default.Test);
			comboBoxTest.SelectedIndex = index == -1 ? 0 : index;
		}

		#endregion

		#region Overview

		/// <summary>
		/// When "Start" button is clicked, fetch settings, create equipment/test/DUTs, start test.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void buttonStart_Click(object sender, EventArgs e)
		{
			try
			{
				// Ensure the user has selected a range and test.
				if ((comboBoxRange.SelectedItem == null) ||
					(comboBoxTest.SelectedItem == null))
				{
					throw new Exception("Please select range and test before starting test.");
				}

				//
				// Reload the settings files in case they changed since the app was started.
				//

				EquipmentSettings equipmentSettings = Settings.Load<EquipmentSettings>(Properties.Settings.Default.SystemSettingsFile);
				if (equipmentSettings == null)
				{
					throw new Exception("Equipment settings not found.  Please contact Engineering.");
				}

				DutSettings dutSettings = Settings.Load<DutSettings>(Properties.Settings.Default.DutSettingsFile);
				RangeSetting rangeSetting = dutSettings.RangeSettings.Find(i => i.Label == comboBoxRange.Text);
				if (rangeSetting == null)
				{
					throw new Exception("Range settings not found. Please contact Engineering.");
				}

				TestSettings testSettings = Settings.Load<TestSettings>(Properties.Settings.Default.TestSettingsFile);
				TestSetting testSetting = testSettings.Tests.Find(i => i.Label == comboBoxTest.Text);
				if (testSetting == null)
				{
					throw new Exception("Test settings not found. Please contact Engineering.");
				}

				//
				// Disable most of the user controls.
				//

				// TODO:  Delete DUT tabs (if they exist) and any data on them.
				// TODO:  Clear the DUT data on the Overview tab.

				comboBoxModel.Enabled = false;
				comboBoxRange.Enabled = false;
				comboBoxTest.Enabled = false;
				checkBoxSelectAll.Enabled = false;
				foreach (Control c in tableLayoutPanelDevicesUnderTest.Controls)
				{
					c.Enabled = false;
				}
				buttonStop.Enabled = true;
				buttonStart.Enabled = false;

				//
				// Create objects for equipment, test, and DUTs.
				//

				_equipment = new Equipment(equipmentSettings);

				_duts.Clear();
				for (uint i = 0; i < NumDuts; i++)
				{
					// Fetch user settings for DUT.
					CheckBox checkBox = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_CHECKBOX, (int)i) as CheckBox;
					TextBox textBoxSerial = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_SERIALNUM, (int)i) as TextBox;
					ComboBox comboBoxModel = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_MODEL, (int)i) as ComboBox;

					ModelSetting modelSetting = dutSettings.ModelSettings.Find(j => j.Label == comboBoxModel.Text);
					if (modelSetting == null)
					{
						throw new Exception("Model settings not found. Please contact Engineering.");
					}

					Dut dut = new Dut(modelSetting)
					{
						SetSerialNumber = SetDutSerialNumber,
						SetStatus = SetDutStatus,
					};
					dut.Device.Index = i + 1;
					dut.Device.Selected = checkBox.Checked;
					dut.Device.Status = DutStatus.Init;
					dut.Device.SerialNumber = textBoxSerial.Text;
					dut.Device.Message = string.Empty;
					_duts.Add(dut);
				}

				_test = new Test(testSetting, _equipment, _duts)
				{
					Finished = TestFinished,
					Update = TestUpdate
				};

				// Start the test.
				_test.Start();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error");
			}
		}

		/// <summary>
		/// When the "Stop" button is clicked, run the Stop action.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void buttonStop_Click(object sender, EventArgs e)
		{
			ConfirmAbort();
		}

		/// <summary>
		/// Before exiting, check the user's wishes and safely end testing.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void FormCalibration_FormClosing(object sender, FormClosingEventArgs e)
		{
			// If a test exists and is running...
			if ((_test != null) && (_test.IsBusy()))
			{
				// Cancel application shutdown.
				e.Cancel = true;

				// If the user chooses to abort the test...
				if (ConfirmAbort() == true)
				{
					// Remember to close the application after the test finishes.
					_closeAfterTest = true;
				}
			}

			// Initialize or clear DUT selections.
			if (Properties.Settings.Default.DutSelections == null)
			{
				Properties.Settings.Default.DutSelections = new System.Collections.Specialized.StringCollection();
			}
			else
			{
				Properties.Settings.Default.DutSelections.Clear();
			}

			// Remember DUT selections.
			for (int i = 0; i < NumDuts; i++)
			{
				CheckBox checkBox = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_CHECKBOX, i) as CheckBox;
				Properties.Settings.Default.DutSelections.Add(checkBox.Checked ? "true" : "false");
			}

			// Initialize or clear Model selections.
			if (Properties.Settings.Default.ModelSelections == null)
			{
				Properties.Settings.Default.ModelSelections = new System.Collections.Specialized.StringCollection();
			}
			else
			{
				Properties.Settings.Default.ModelSelections.Clear();
			}

			// Remember Model selections.
			for (int i = 0; i < NumDuts; i++)
			{
				ComboBox comboBox = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_MODEL, i) as ComboBox;
				Properties.Settings.Default.ModelSelections.Add(comboBox.SelectedItem.ToString());
			}

			// Save settings.
			Properties.Settings.Default.Save();
		}

		/// <summary>
		/// When the "Model" selection is changed, save the new selection and fetch new ranges.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void comboBoxModel_SelectedIndexChanged(object sender, EventArgs e)
		{
			// Remember the selected value.
			Properties.Settings.Default.Model = comboBoxModel.SelectedItem.ToString();

			// Update the individual selections for all DUTs.
			for (int i = 0; i < NumDuts; i++)
			{
				ComboBox comboBox = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_MODEL, i) as ComboBox;
				int j = comboBox.FindStringExact(comboBoxModel.SelectedItem.ToString());
				comboBox.SelectedIndex = j == -1 ? 0 : j;
			}
		}

		/// <summary>
		/// When the "Range" selection is changed, save the new selection.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void comboBoxRange_SelectedIndexChanged(object sender, EventArgs e)
		{
			// Remember the selected value.
			Properties.Settings.Default.Range = comboBoxRange.SelectedItem.ToString();
		}

		/// <summary>
		/// When the "Test" selection is changed, save the new selection.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void comboBoxTest_SelectedIndexChanged(object sender, EventArgs e)
		{
			// Remember the selected value.
			Properties.Settings.Default.Test = comboBoxTest.SelectedItem.ToString();
		}

		/// <summary>
		/// When "Select/deselect all" checkbox is clicked, select/deselect all DUTs.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void checkBoxSelectAll_CheckedChanged(object sender, EventArgs e)
		{
			// Look through each control.
			foreach (Control c in tableLayoutPanelDevicesUnderTest.Controls)
			{
				// If it's a checkbox...
				if (c is CheckBox cb)
				{
					// Make its state match the select all checkbox.
					cb.Checked = ((CheckBox)sender).Checked;
				}
			}
		}

		/// <summary>
		/// Before exiting, check the user's wishes and safely end testing.
		/// </summary>
		/// <returns>true if we're quitting; false if cancelled</returns>
		private bool ConfirmAbort()
		{
			DialogResult result = DialogResult.OK;  // whether to quit or not

			// If a test exists and is running...
			if ((_test != null) && (_test.IsBusy()))
			{
				// Ask the user if they really want to stop the test.
				result = MessageBox.Show("Abort the test?", "Abort", MessageBoxButtons.OKCancel);
			}

			// If we're quitting, cancel a test.
			// Don't update GUI; the "TestFinished" method will do that.
			if (result == DialogResult.OK)
			{
				try
				{
					_test?.Stop();
				}
				catch (Exception ex)
				{
					MessageBox.Show("Could not stop test." + Environment.NewLine + ex.Message, "Error");
				}
			}

			// Return whether or not we're stopping the test.
			return (result == DialogResult.OK);
		}

		#endregion

		#region File Menu

		/// <summary>
		/// When File --> Exit menu item is clicked, close the application.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void exitToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// This will invoke the "FormClosing" action, so nothing else to do here.

			// Exit the application.
			Application.Exit();
		}

		#endregion

		#region Equipment Menu

		private void InitEquipmentMenu(Type type)
		{
			// Populate the equipment menu.
			List<Type> controlTypes = Utilities.FindInterfaces(type);
			foreach (Type t in controlTypes)
			{
				// Add the class of device.
				ToolStripDropDownItem equipmentType = new ToolStripMenuItem(t.GetDescription());
				equipmentToolStripMenuItem.DropDownItems.Add(equipmentType);

				// Find the applicable devices.
				List<Type> deviceTypes = Utilities.FindClasses(t);
				foreach (Type d in deviceTypes)
				{
					equipmentType.DropDownItems.Add(d.GetDescription(), null, menuEquipment_Click);
				}
			}
		}

		private void menuEquipment_Click(object sender, EventArgs e)
		{
			// Find the parent menu item, then children items.
			Control parent = ((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;

			if (((ToolStripMenuItem)sender).Checked)
			{
				((ToolStripMenuItem)sender).Checked = false;
			}
			else
			{
				((ToolStripMenuItem)sender).Checked = true;
			}
		}

		#endregion

		#region Settings Menu

		/// <summary>
		/// When Edit --> Number of DUTs is selected, prompt the user to select the number of DUTS.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void numberOfDUTsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// Prompt user to enter desired number of DUTs (current value as default).
			int numDuts = NumDuts;
			DialogResult result = InputDialog.Numeric("Number of DUTs", ref numDuts, 1, 24);

			// Update the property (which will also update the form).
			NumDuts = numDuts;
		}

		/// <summary>
		/// When Settings --> Log Directory is selected, prompt the user to
		/// select the directory where test results are stored.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void logDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// Set the path first shown to the user to be the currently selected one.
			folderBrowserDialog1.SelectedPath = Properties.Settings.Default.LogDirectory;

			// Prompt the user to select a folder for output files.
			if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
			{
				Properties.Settings.Default.LogDirectory = folderBrowserDialog1.SelectedPath;
			}
		}

		/// <summary>
		/// Present a settings file to the user to edit.
		/// </summary>
		/// <param name="filename"></param>
		private void EditSettings<T>(string filename)
			where T : Attribute
		{
			try
			{
				// Fetch equipment settings.
				T settings = Settings.Load<T>(filename);

				// Create and show a new object editor with the equipment settings.
				FormObjectEditor objectEditor = new FormObjectEditor();
				objectEditor.AddObject<T>(settings, "Label");
				DialogResult result = objectEditor.ShowDialog();

				// If user selects "OK," save the settings.
				if (result == DialogResult.OK)
				{
					Settings.Save(objectEditor.FetchObject<T>(), filename);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Could not edit settings." + Environment.NewLine + ex.Message, "Error");
			}
		}

		/// <summary>
		/// When Tools --> Equipment Settings menu is clicked, open an object browser for the settings.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void equipmentSettingsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			EditSettings<EquipmentSettings>(Properties.Settings.Default.SystemSettingsFile);
		}

		/// <summary>
		/// When Tools --> DUT Settings menu is clicked, open an object browser for the settings.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void dUTSettingsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			EditSettings<DutSettings>(Properties.Settings.Default.DutSettingsFile);
		}

		/// <summary>
		/// When Tools --> Test Settings menu is clicked, open an object browser for the settings.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void testSettingsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			EditSettings<TestSettings>(Properties.Settings.Default.TestSettingsFile);
		}

		#endregion

		#region Help Menu

		/// <summary>
		/// When the user clicks Help --> About, show an about box.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// Create an about box.
			FormAbout formAbout = new FormAbout();

			// Show the about box.
			// ShowDialog() disables interaction with the app's other forms.
			// Show() does not.
			formAbout.ShowDialog();
		}

		#endregion

		#region Public Methods

		/// <summary>
		/// Update the form with the test's status.
		/// </summary>
		/// <param name="message"></param>
		public void TestUpdate(int percent, string message)
		{
			// Update the progress bar.
			toolStripProgressBar1.Value = percent;

			// Update the status message.
			toolStripStatusLabel1.Text = message;
		}

		/// <summary>
		/// Reset the form after a test is completed or cancelled.
		/// </summary>
		public void TestFinished()
		{
			// Enable most of the controls.
			comboBoxModel.Enabled = true;
			comboBoxRange.Enabled = true;
			comboBoxTest.Enabled = true;
			checkBoxSelectAll.Enabled = true;
			foreach (Control c in tableLayoutPanelDevicesUnderTest.Controls)
			{
				c.Enabled = true;
			}

			// Enable the "Start" button and disable the "Stop" button.
			buttonStart.Enabled = true;
			buttonStop.Enabled = false;

			// If requested, close the application.
			if (_closeAfterTest)
			{
				Application.Exit();
			}

			// Update the progress bar.
			toolStripProgressBar1.Value = 0;

			// Update the status message.
			toolStripStatusLabel1.Text = "Ready...";
		}

		public void SetDutStatus(uint dut, DutStatus status)
		{
			// Find the applicable DUT status textbox.
			// Remember that table layout panel has 0-based index, while DUTs have 1-based index.
			Label labelStatus = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_STATUS, (int)dut - 1) as Label;

			// If called from a different thread than the form, invoke the method on the form's thread.
			// https://stackoverflow.com/questions/142003/cross-thread-operation-not-valid-control-accessed-from-a-thread-other-than-the
			if (labelStatus.InvokeRequired)
			{
				labelStatus.Invoke(new MethodInvoker(delegate { SetDutStatus(dut, status); }));
			}
			else
			{
				// Set the status text, and use bold text.
				labelStatus.Text = status.GetDescription();
				labelStatus.Font = new Font(labelStatus.Font, FontStyle.Bold);

				// Apply formatting.
				switch (status)
				{
					case DutStatus.Pass:
						labelStatus.ForeColor = Color.Green;
						break;
					case DutStatus.Found:
						labelStatus.ForeColor = Color.Blue;
						break;
					case DutStatus.Fail:
					case DutStatus.NotFound:
					case DutStatus.PortError:
						labelStatus.ForeColor = Color.Red;
						break;
				}
			}
		}

		public void SetDutSerialNumber(uint dut, string serialNumber)
		{
			// Find the applicable DUT serial number textbox.
			TextBox textBoxSerialNumber = tableLayoutPanelDevicesUnderTest.GetControlFromPosition(DUT_COLUMN_SERIALNUM, (int)dut - 1) as TextBox;

			// If called from a different thread than the form, invoke the method on the form's thread.
			if (textBoxSerialNumber.InvokeRequired)
			{
				textBoxSerialNumber.Invoke(new MethodInvoker(delegate { SetDutSerialNumber(dut, serialNumber); }));
			}
			else
			{
				// Set the text.
				textBoxSerialNumber.Text = serialNumber;
			}
		}

		#endregion
	}
}
