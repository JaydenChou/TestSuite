﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using Sensit.TestSDK.Calculations;
using Sensit.TestSDK.Communication;
using Sensit.TestSDK.Exceptions;
using Sensit.TestSDK.Interfaces;

namespace Sensit.TestSDK.Devices
{
	/// <summary>
	/// Communication driver for Cole-Parmer Mass Flow Controller.
	/// </summary>
	/// <remarks>
	/// Product website:
	/// https://www.coleparmer.com/p/cole-parmer-mass-flow-controllers-for-gas/43456
	/// Instruction manual:
	/// https://pim-resources.coleparmer.com/instruction-manual/cole-parmer-mass-flow-controller-manual.pdf
	/// 
	/// The following info came from Cole-Parmer tech support, and is not in the manual:
	/// 
	/// To set control variable, adjust via menu setting or via register 122.
	/// The command to read the register is ID + R + 122, and to write the
	/// register is ID + W + 122.  For a unit with ID "A", the commands would
	/// be "AR122" and "AW122" respectively.
	/// 
	/// The return value from register 122 will be one of the following:
	/// 0 or 1 - indicates control variable has been set via menu.
	/// 34 - control variable is absolute pressure
	/// 36 - control variable is volume flow
	/// 37 - control variable is mass flow
	/// 
	/// The device was not designed to measure mass flow without controlling it,
	/// but you can do just that for SHORT PERIODS by sending a large mass flow
	/// setpoint to the device and not providing enough gas to meet that setpoint.
	/// This will cause the controller to fully open the valve in an attempt to
	/// increase flow, and you'll be able to read the flow without the valve in
	/// the way.  However, THE VALVE WILL BE STRESSED AND BE DAMAGED OVER TIME.
	/// Colloquially, this damage takes more than a day, so this is fine for small tests.
	/// 
	/// Also, there are two firmware versions that I've seen.  They have identical
	/// serial interfaces, but some menu differences and major electronics differences.
	/// 
	/// </remarks>
	public class ColeParmerMFC : SerialDevice, IMassFlowController,
		IMassFlowReference, IVolumeFlowReference, ITemperatureReference, IPressureReference
	{
		/// <summary>
		/// Commands sent to the device
		/// </summary>
		private static class Command
		{
			public const string SetAddress = "*@=";   // set the device address; set polling/streaming mode
			public const string SetSetpoint = "S";    // set the flow setpoint
			public const string ReadRegister = "*R";  // read a register (low-level; not typically used)
			public const string WriteRegister = "*W"; // write a register (low-level; not typically used)
			public const string GasSelect = "$$";     // select gas (for conversion from volume flow to mass flow)
		}

		/// <summary>
		/// Commands to set and query gas selection.
		/// </summary>
		/// <remarks>
		/// The "Index" is the code sent to the device to set the gas.
		/// The "Code" is what the device sends back when queried to indicate the current setting.
		/// Inspiration for this dictionary with a tuple:
		/// https://stackoverflow.com/questions/569903/multi-value-dictionary
		/// https://stackoverflow.com/questions/8002455/how-to-easily-initialize-a-list-of-tuples
		/// Note this may require .NET framework 7.0 or later to compile.
		/// </remarks>
		private static readonly Dictionary<Gas, (int Index, string Code)> GasCommand = new Dictionary<Gas, (int Index, string Code)>
		{
			{ Gas.Air,                  (0, "Air") },
			{ Gas.Argon,                (1, "Ar") },
			{ Gas.Methane,              (2, "CH4") },
			{ Gas.CarbonMonoxide,       (3, "CO") },
			{ Gas.CarbonDioxide,        (4, "CO2") },
			{ Gas.Ethane,               (5, "C2H6") },
			{ Gas.Hydrogen,             (6, "H2") },
			{ Gas.Helium,               (7, "He") },
			{ Gas.Nitrogen,             (8, "N2") },
			{ Gas.NitrousOxide,         (9, "N2O") },
			{ Gas.Neon,                 (10, "Ne") },
			{ Gas.Oxygen,               (11, "O2") },
			{ Gas.Propane,              (12, "C3H8") },
			{ Gas.normalButane,         (13, "nC4H10") },
			{ Gas.Acetylene,            (14, "C2H2") },
			{ Gas.Ethylene,             (15, "C2H4") },
			{ Gas.isoButane,            (16, "iC4H10") }, // (code in manual has typo)
			{ Gas.Krypton,              (17, "Kr") },
			{ Gas.Xenon,                (18, "Xe") },
			{ Gas.SulfurHexafluoride,   (19, "SF6") },
			{ Gas.C25,                  (20, "C-25") },
			{ Gas.C10,                  (21, "C-10") },
			{ Gas.C8,                   (22, "C-8") },
			{ Gas.C2,                   (23, "C-2") },
			{ Gas.C75,                  (24, "C-75") },
			{ Gas.He25,                 (25, "He-25") }, // (code in manual has typo)
			{ Gas.He75,                 (26, "He-75") }, // (code in manual has typo)
			{ Gas.A1025,                (27, "A1025") },
			{ Gas.Star29,               (28, "Star29") },
			{ Gas.P5,                   (29, "P-5") },
		};

		// specifier for a specific device on the serial port
		private const char ADDRESS = 'A';

		private double _massFlowSetpoint = 0.0;

		/// <summary>
		/// Enable the mass flow controller's streaming mode.
		/// </summary>
		/// <remarks>
		/// Not used; the method was written to document the command. If you
		/// ever use it, you should probably add a check that the command succeeds.
		/// </remarks>
		private void SetStreaming()
		{
			try
			{
				// "*@=@" sets the device into streaming mode.
				Port.WriteLine(Command.SetAddress + '@');
			}
			catch (SystemException ex)
			{
				throw new DevicePortException("Could not set mass flow controller streaming mode:"
					+ Environment.NewLine + ex.Message);
			}
		}

		/// <summary>
		/// Set gas type (needed for accurate conversion between volumetric and mass flow).
		/// </summary>
		public void SetGas()
		{
			// Note the way we access the "GasSelection" Dictionary/Tuple.
			string msg = ADDRESS + Command.GasSelect + GasCommand[GasSelection].Index;

			try
			{
				// "A$$12" selects propane gas for device A.
				Port.WriteLine(msg);

				// Read the response from the serial port (until we get a \r character).
				string message = Port.ReadLine();

				// Split the string using spaces to separate each word.
				char[] separators = new char[] { ' ' };
				string[] words = message.Split(separators, StringSplitOptions.RemoveEmptyEntries);
				string gasResult = words[6];

				// Check the gas selection.
				// Note (again) the way we access the "GasSelection" Dictionary/Tuple.
				string gasRequest = GasCommand[GasSelection].Code;
				if (string.Compare(gasResult, gasRequest) != 0)
				{
					throw new DeviceCommunicationException("Could not write gas selection to mass flow controller."
						+ Environment.NewLine + "Value returned from instrument was incorrect.");
				}
			}
			catch (InvalidOperationException ex)
			{
				throw new DevicePortException("Could not write gas selection to mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
			catch (TimeoutException ex)
			{
				throw new DeviceCommunicationException("No response from mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
		}

		public void SetGas(Gas selection)
		{
			GasSelection = selection;

			SetGas();
		}

		/// <summary>
		/// Set the device address
		/// </summary>
		/// <remarks>
		/// Must be set when no other devices are on the bus, or it will
		/// affect all devices at the same time.  This class supports only one
		/// device per bus, so a constant address is used.
		/// </remarks>
		private void WriteAddress()
		{
			try
			{
				// "*@=A" sets address = A (and puts device into polling mode).
				Port.WriteLine(Command.SetAddress + ADDRESS);

				// Read from the serial port (until we get a \r character).
				string message = Port.ReadLine();

				// Split the string using spaces to separate each word.
				char[] separators = new char[] { ' ' };
				string[] words = message.Split(separators, StringSplitOptions.RemoveEmptyEntries);

				// Check the address.
				if (string.Compare(words[0], ADDRESS.ToString()) != 0)
				{
					throw new DeviceCommunicationException("Could not update mass flow controller address"
						+ Environment.NewLine + "Value returned from instrument was incorrect.");
				}
			}
			catch (InvalidOperationException ex)
			{
				throw new DevicePortException("Could not update mass flow controller address."
					+ Environment.NewLine + ex.Message);
			}
			catch (TimeoutException ex)
			{
				throw new DeviceCommunicationException("No response from mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
		}

		#region Serial Device Methods

		public new int BaudRate
		{
			get => Port.BaudRate;
			set
			{
				if ((value != 2400) &&
					(value != 9600) &&
					(value != 19200) &&
					(value != 38400) &&
					(value != 57600))
				{
					throw new DeviceSettingNotSupportedException("The Cole Parmer MFC does not support baud rate " + value.ToString() + ".");
				}

				Port.BaudRate = value;
			}
		}

		public new int DataBits
		{
			get => Port.DataBits;
			set
			{
				if (value != 8)
				{
					throw new DeviceSettingNotSupportedException("The Cole Parmer MFC only supports 8 data bits.");
				}

				Port.DataBits = value;
			}
		}

		public new Parity Parity
		{
			get => Port.Parity;
			set
			{
				if (value != Parity.None)
					throw new DeviceSettingNotSupportedException("The Cole Parmer MFC does not support parity.");

				Port.Parity = value;
			}
		}

		public new StopBits StopBits
		{
			get => Port.StopBits;
			set
			{
				if (value != StopBits.One)
					throw new DeviceSettingNotSupportedException("The Cole Parmer MFC only supports one stop bit.");

				Port.StopBits = value;
			}
		}

		public override void WriteSerialProperties(int dataBits = 8, Parity parity = Parity.None, StopBits stopBits = StopBits.One)
		{
			// This mass flow controller only supports its default settings,
			// so there is nothing to do here except update the properties.
			DataBits = dataBits;
			Parity = parity;
			StopBits = stopBits;
		}

		/// <summary>
		/// Open the serial port with the correct settings; set default address (and polling mode).
		/// </summary>
		/// <param name="portName">com port name (i.e. "COM3")</param>
		/// <param name="baudRate">baud rate (9600 and 19200 are supported)</param>
		public override void Open(string portName, int baudRate = 19200)
		{
			try
			{
				// Set serial port settings.
				Port.PortName = portName;
				Port.BaudRate = baudRate;
				Port.DataBits = 8;
				Port.Parity = Parity.None;
				Port.StopBits = StopBits.One;
				Port.Handshake = Handshake.None;
				Port.ReadTimeout = 500;
				Port.WriteTimeout = 500;

				// Messages are terminated with a carriage return.
				Port.NewLine = "\r";

				// Open the serial port.
				Port.Open();

				// Set polling mode (by assigning a device address).
				WriteAddress();
			}
			catch (SystemException ex)
			{
				throw new DevicePortException("Could not open mass flow controller's serial port."
					+ Environment.NewLine + ex.Message);
			}
		}

		#endregion

		#region Reference Device Methods

		public Dictionary<VariableType, double> Readings { get; } = new Dictionary<VariableType, double>
		{
			{ VariableType.MassFlow, 0.0 },
			{ VariableType.Pressure, 0.0 },
			{ VariableType.Temperature, 0.0 },
			{ VariableType.VolumeFlow, 0.0 }
		};

		public UnitOfMeasure.Flow FlowUnit { get; set; } = UnitOfMeasure.Flow.CubicFeetPerMinute;

		public Gas GasSelection { get; set; } = Gas.Air;

		public UnitOfMeasure.Temperature TemperatureUnit { get; set; } = UnitOfMeasure.Temperature.Celsius;

		public UnitOfMeasure.Pressure PressureUnit { get; set; } = UnitOfMeasure.Pressure.PSI;

		public void Read()
		{
			try
			{
				// Read (when in polling mode) by sending the device address.
				Port.WriteLine(ADDRESS.ToString());

				// Read from the serial port (until we get a \r character).
				string message = Port.ReadLine();

				// Split the string using spaces to separate each word.
				char[] separators = new char[] { ' ' };
				string[] words = message.Split(separators, StringSplitOptions.RemoveEmptyEntries);

				// Check the address.
				if (string.Compare(words[0], ADDRESS.ToString()) != 0)
				{
					throw new DeviceCommunicationException("Could not read from mass flow controller."
					+ Environment.NewLine + "Incorrect device ID");
				}

				// Figure out which is which and update class properties.
				Readings[VariableType.Pressure] = Convert.ToSingle(words[1]);
				Readings[VariableType.Temperature] = Convert.ToSingle(words[2]);
				Readings[VariableType.VolumeFlow] = Convert.ToSingle(words[3]);
				Readings[VariableType.MassFlow] = Convert.ToSingle(words[4]);

				// Probably don't update the control properties.
				//Setpoint = Convert.ToSingle(words[5]);
				//GasSelection = words[6];
			}
			catch (InvalidOperationException ex)
			{
				throw new DevicePortException("Could not read from mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
			catch (TimeoutException ex)
			{
				throw new DeviceCommunicationException("No response from mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
		}

		#endregion

		#region Control Device Methods

		public void WriteSetpoint(VariableType type, double setpoint)
		{
			if (type != VariableType.MassFlow)
			{
				throw new DeviceSettingNotSupportedException("Cole Parmer MFC does not support requested setpoint.");
			}

			// Check for valid setpoint values.
			if (setpoint < 0.0)
			{
				throw new DeviceOutOfRangeException("Mass Flow Controller setpoint must be greater than or equal to 0."
					+ Environment.NewLine + "Attempted setpoint was:  " + setpoint);
			}

			_massFlowSetpoint = setpoint;

			WriteMassFlowSetpoint(type, _massFlowSetpoint);
		}

		private void WriteMassFlowSetpoint(VariableType type, double setpoint)
		{
			try
			{
				// "AS4.54" = Set setpoint to 4.54 on device A.
				Port.WriteLine(ADDRESS + Command.SetSetpoint + setpoint.ToString());

				// Read the response from the serial port (until we get a \r character).
				string message = Port.ReadLine();

				// Split the string using spaces to separate each word.
				char[] separators = new char[] { ' ' };
				string[] words = message.Split(separators, StringSplitOptions.RemoveEmptyEntries);
				float returnedValue = Convert.ToSingle(words[5]);

				// Check the setpoint.
				// Had trouble with this statment.  Neither Double.Epsilon or double.Equals work.
				// So I ensure the difference is less than 1.
				if (Math.Abs(returnedValue - setpoint) > 1.0)
				{
					throw new DeviceCommunicationException("Could not write setpoint (" + setpoint.ToString() + ") to mass flow controller."
						+ Environment.NewLine + "Value read from instrument (" + returnedValue.ToString() + ") was incorrect.");
				}
			}
			catch (InvalidOperationException ex)
			{
				throw new DevicePortException("Could not write setpoint to mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
			catch (TimeoutException ex)
			{
				throw new DeviceCommunicationException("No response from mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
		}

		public double ReadSetpoint(VariableType type)
		{
			return _massFlowSetpoint;
		}

		private double ReadMassFlowSetpoint(VariableType type)
		{
			try
			{
				// Read (when in polling mode) by sending the device address.
				Port.WriteLine(ADDRESS.ToString());

				// Read from the serial port (until we get a \r character).
				string message = Port.ReadLine();

				// Split the string using spaces to separate each word.
				char[] separators = new char[] { ' ' };
				string[] words = message.Split(separators, StringSplitOptions.RemoveEmptyEntries);

				// Convert the setpoint to a number and set the property.
				double setpoint = Convert.ToSingle(words[5]);

				// Return the setpoint in case the user wants it.
				return setpoint;
			}
			catch (InvalidOperationException ex)
			{
				throw new DevicePortException("Could not read setpoint from mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
			catch (TimeoutException ex)
			{
				throw new DeviceCommunicationException("No response from mass flow controller."
					+ Environment.NewLine + ex.Message);
			}
			catch (FormatException ex)
			{
				throw new DeviceCommunicationException("Could not read setpoint from mass flow controller."
					+ Environment.NewLine + "Incorrect response."
					+ Environment.NewLine + ex.Message);
			}
		}

		public void SetControlMode(ControlMode mode)
		{
			switch (mode)
			{
				case ControlMode.Passive:
					// There's not really a measure mode; just set a setpoint of zero.
					WriteMassFlowSetpoint(VariableType.MassFlow, 0.0);
					break;
				case ControlMode.Active:
					// In control mode, just update the setpoint.
					WriteMassFlowSetpoint(VariableType.MassFlow, _massFlowSetpoint);
					break;
				default:
					throw new DeviceSettingNotSupportedException("Cannot set mass flow controller control mode:"
						+ Environment.NewLine + "Unrecognized mode.");
			}
		}

		#endregion
	}
}
