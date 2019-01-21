﻿using System.Collections.Generic;
using Sensit.TestSDK.Devices;
using Sensit.TestSDK.Interfaces;
using Sensit.TestSDK.Settings;

namespace Sensit.App.Calibration
{
	/// <summary>
	/// Possible reference devices in our test system.
	/// </summary>
	public enum Reference
	{
		MassFlow,
		VolumeFlow,
		Velocity,
		Pressure,
		Temperature
	}

	/// <summary>
	/// Possible control devices in our test system.
	/// </summary>
	public enum Controller
	{
		MassFlow,
		VolumeFlow,
		Velocity,
		Pressure,
		Temperature
	}

	public class Equipment
	{
		private EquipmentSettings _settings;							// system settings
		private MFC _mfc;												// mass flow controller
		private Keysight_34972A _datalogger;							// datalogger (for analog sensor DUTs)

		public Equipment()
		{
			// Read system settings.
			_settings = Settings.Load<EquipmentSettings>(Properties.Settings.Default.SystemSettingsFile);

			// Create test equipment objects.
			// TODO:  Do this according to settings, (i.e. use manual if selected).
			_mfc = new MFC();
			_datalogger = new Keysight_34972A();
		}

		public IDutInterfaceDevice DutInterface => _datalogger;

		public IMassFlowController MassFlowController => _mfc;

		public IMassFlowReference MassFlowReference => _mfc;

		public void Open()
		{
			// Open all devices.
			_mfc.Open(_settings?.MassFlowControllerPort);
			_datalogger.Open();
		}

		public void Close()
		{
			// Close all devices.
			_mfc?.Close();
			_datalogger?.Close();
		}

		/// <summary>
		/// Read all reference devices.
		/// </summary>
		/// <returns>collection of all readings, searchable by type</returns>
		public Dictionary<Reference, double> Read()
		{
			// Read all test equipment.
			_mfc.Read();

			// TODO:  Read datalogger if selected.
			//_datalogger.ReadAnalog();

			// Fetch the readings and return them.
			Dictionary<Reference, double> readings = new Dictionary<Reference, double>
			{
				{ Reference.MassFlow, _mfc.MassFlow }
			};

			return readings;
		}
	}
}
