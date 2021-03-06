﻿#region License
/* 
 * Copyright (C) 2013 Myrcon Pty. Ltd. / Geoff "Phogue" Green
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to 
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in 
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using System.Net;
using System.Diagnostics;
using Squad.Admin.Console.Utilities;
using Squad.Admin.Console.RCON;


namespace Squad.Admin.Console.Forms
{
    delegate void ClearGrid(DataGridView gridControl);
    delegate void ClearListBox(ListBox listboxControl);
    delegate void SetControlEnabled(Control control, bool isEnabled);
    delegate void AddGridRow(string slot, string name, string steamId, string status, string disconnectTime);
    delegate void AddListboxItem(string command);
    delegate void AddTextToTextbox(TextBox textbox, string response);

    public partial class frmMainConsole : Form
    {

        ServerConnectionInfo serverConnectionInfo = new ServerConnectionInfo();
        RconConnection serverRconConnection = new RconConnection();
        XDocument menuReasons;

        public frmMainConsole()
        {
            InitializeComponent();

            this.serverRconConnection.ConnectionSuccess += ServerRconConnection_ConnectionSuccess;
            this.serverRconConnection.ServerResponseReceived += ServerRconConnection_ServerResponseReceived;
            this.serverRconConnection.ServerError += ServerRconConnection_ServerError;

            // Bind control event handlers
            this.txtServerIP.Validating += TxtServerIP_Validating;
            this.txtServerPort.Validating += TxtServerPort_Validating;
            this.txtRconPassword.Validating += TxtRconPassword_Validating;
            this.grdPlayers.CellContentClick += GrdPlayers_CellContentClick;
            this.grdPlayers.MouseClick += GrdPlayers_MouseClick;

            LoadAutocompleteCommands();
            
        }

        #region control validation events

        private void TxtRconPassword_Validating(object sender, CancelEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            this.serverConnectionInfo.Password = tb.Text.Trim();
            btnConnect.Enabled = this.serverConnectionInfo.IsValid();
        }

        private void TxtServerPort_Validating(object sender, CancelEventArgs e)
        {
            MaskedTextBox tb = (MaskedTextBox)sender;
            this.serverConnectionInfo.ServerPort = Convert.ToInt32(tb.Text.Trim());
            btnConnect.Enabled = this.serverConnectionInfo.IsValid();
        }

        private void TxtServerIP_Validating(object sender, CancelEventArgs e)
        {
            IPAddress ip;
            TextBox tb = (TextBox)sender;
            if (IPAddress.TryParse(tb.Text.Trim(), out ip))
            {
                this.serverConnectionInfo.ServerIP = ip;
            }
            btnConnect.Enabled = this.serverConnectionInfo.IsValid();
        }

        #endregion

        #region control event handlers

        private void btnConnect_Click(object sender, EventArgs e)
        {
            this.serverRconConnection.Connect(new IPEndPoint(this.serverConnectionInfo.ServerIP, this.serverConnectionInfo.ServerPort), this.serverConnectionInfo.Password);
            LoadContextMenuItems();
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            ClearGridRows(grdPlayers);
            EnableLoginControls(false);
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            // turn on or off the password mask by checking the checkbox
            txtRconPassword.UseSystemPasswordChar = !chkShowPassword.Checked;
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            this.serverRconConnection.ServerCommand("ListPlayers");
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            this.serverRconConnection.ServerCommand(txtCommand.Text);
            AddCommandToHistoryList(txtCommand.Text);
            ClearCommandText(txtCommand, string.Empty);
            SetControlEnabledState(btnClear, true);
        }


        private void btnClear_Click(object sender, EventArgs e)
        {
            ClearCommandHistory(lstHistory);
            SetControlEnabledState(btnClear, false);
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {

        }

        // Launch the browser with the Steam profile of the selected player
        private void GrdPlayers_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            string sUrl = "http://steamcommunity.com/profiles/" + grdPlayers.Rows[e.RowIndex].Cells[2].Value.ToString();
            ProcessStartInfo sInfo = new ProcessStartInfo(sUrl);
            Process.Start(sInfo);
        }

        /// <summary>
        /// Menu is dynamically built each time a row is clicked on. The "Tag" property of each menu item is used to store
        /// the actual RCON command to be executed if a menu item is selected.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GrdPlayers_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int currentMouseOverRow = grdPlayers.HitTest(e.X, e.Y).RowIndex;

                // pull the current player name from the clicked row
                string playerName = grdPlayers.Rows[currentMouseOverRow].Cells[1].Value.ToString();
                string playerId = grdPlayers.Rows[currentMouseOverRow].Cells[2].Value.ToString();
                string adminName = txtDisplayName.Text.Trim().Length != 0 ? txtDisplayName.Text.Trim() : "RCON Admin";

                if (currentMouseOverRow >= 0)
                {
                    ContextMenu m = new ContextMenu();

                    // Warnings
                    MenuItem wi = new MenuItem("Warn");

                    foreach(XElement w in menuReasons.Root.Element("WarnReasons").Elements())
                    {
                        MenuItem warn = new MenuItem(w.Value.Replace("PLAYERNAME", playerName).Replace("ADMINNAME", adminName));
                        warn.Tag = "AdminBroadcast " + warn.Text;
                        warn.Click += menu_Click;
                        wi.MenuItems.Add(warn);
                    }
                    m.MenuItems.Add(wi);
                    
                    // Kicks
                    MenuItem ki = new MenuItem("Kick");

                    foreach (XElement k in menuReasons.Root.Element("KickReasons").Elements())
                    {
                        MenuItem kick = new MenuItem(k.Value.Replace("PLAYERNAME", playerName).Replace("ADMINNAME", adminName));
                        kick.Tag = "AdminKickById " + playerId + " " + kick.Text;
                        kick.Click += menu_Click;
                        ki.MenuItems.Add(kick);
                    }
                    m.MenuItems.Add(ki);

                    // BansS
                    MenuItem bi = new MenuItem("Ban");

                    foreach (XElement b in menuReasons.Root.Element("BanReasons").Elements())
                    {
                        MenuItem ban = new MenuItem(b.Value.Replace("PLAYERNAME", playerName).Replace("ADMINNAME", adminName));
                        ban.Tag = "AdminBanById " + playerId + " " + ban.Text;
                        ban.Click += menu_Click;
                        bi.MenuItems.Add(ban);
                    }
                    m.MenuItems.Add(bi);

                    m.Show(grdPlayers, new Point(e.X, e.Y));
                }
            }
        }


        void menu_Click(object sender, EventArgs e)
        {
            this.serverRconConnection.ServerCommand(((MenuItem)sender).Tag.ToString());
            AddCommandToHistoryList(((MenuItem)sender).Tag.ToString());
        }

        #endregion

        #region Helpers

        private void EnableLoginControls(bool enable)
        {
            SetControlEnabledState(btnDisconnect, enable);
            SetControlEnabledState(btnConnect, !enable);
            SetControlEnabledState(txtServerIP, !enable);
            SetControlEnabledState(txtServerPort, !enable);
            SetControlEnabledState(txtRconPassword, !enable);
            SetControlEnabledState(txtDisplayName, !enable);
            SetControlEnabledState(chkShowPassword, !enable);
            SetControlEnabledState(grpPlayerList, enable);
            SetControlEnabledState(grpConsole, enable);

        }

        /// <summary>
        /// Fill the Command textbox autocomplete list with all valid commands from the Commands.dat text file
        /// </summary>
        private void LoadAutocompleteCommands()
        {
            AutoCompleteStringCollection commandList = new AutoCompleteStringCollection();

            string[] commands = File.ReadAllLines(AppDomain.CurrentDomain.BaseDirectory + "Commands.dat");

            for (int i = 0; i < commands.Length; i++)
            {
                commandList.Add(commands[i].Trim());
            }

            txtCommand.AutoCompleteCustomSource = commandList;
        }

        /// <summary>
        /// Load context menu options from the menu xml file into 
        /// </summary>
        private void LoadContextMenuItems()
        {
            // Load the xml file with the menu reasons
            try
            {
                menuReasons = XDocument.Load(@"MenuReasons.xml");
            }
            catch(Exception ex)
            {
                MessageBox.Show("Error occurred trying to open the menu options!\r\nError: " + ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            
        }

        #endregion

        #region Command and response handlers

        private void ServerRconConnection_ServerError(string error)
        {
            AddServerResponseText(txtResponse, error);
        }

        /// <summary>
        /// Event raised when a response to a server command is received
        /// </summary>
        /// <param name="commandName"></param>
        /// <param name="commandResponse"></param>
        private void ServerRconConnection_ServerResponseReceived(string commandName, string commandResponse)
        {
            switch(commandName.ToUpper())
            {
                case "LISTPLAYERS":
                    this.ListPlayers(commandResponse);
                    break;
                default:
                    AddServerResponseText(txtResponse, commandResponse);
                    break;
            }
        }

        /// <summary>
        /// Server connnection success received
        /// </summary>
        /// <param name="info"></param>
        private void ServerRconConnection_ConnectionSuccess(bool info)
        {
            EnableLoginControls(true);
            this.serverRconConnection.ServerCommand("ListPlayers");
        }

        public void ListPlayers(string playerList)
        {
            // Remove all rows from the grid
            ClearGridRows(grdPlayers);

            try
            {
                bool d = false;     // flag used to indicate that we are not processing the recently disconnected players
                string currentLine = string.Empty;

                // Take the response and break it into a string array breaking each line off
                string[] playerArray = playerList.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

                for (int i = 0; i < playerArray.Length; i++)
                {
                    // Get the current line
                    currentLine = playerArray[i].Trim();

                    // No blank lines
                    if (currentLine.Length > 0)
                    {
                        // Looking for the list to change to disconnected players - skip this check once we're in the disconnected list
                        if (!d) d = currentLine.Trim().ToUpper() == "----- Recently Disconnected Players [Max of 15] ----".ToUpper();


                        if (!d)
                        {
                            // Process connected player list - skip the first line
                            if (currentLine.Trim().ToUpper() != ("----- Active Players -----").ToUpper())
                            {
                                string[] playerInfo = currentLine.Split(':');

                                string[] slot = playerInfo[1].Split('|');
                                string[] steamId = playerInfo[2].Split('|');
                                string playerName = playerInfo[3];

                                AddPlayerToGrid(slot[0], playerName, steamId[0], "Connected", "");
                            }
                        }
                        else
                        {
                            // Process disconnected player list
                            if (currentLine.Trim().ToUpper() != "----- Recently Disconnected Players [Max of 15] ----".ToUpper())
                            {
                                string[] playerInfo = currentLine.Split(':');

                                string[] slot = playerInfo[1].Split('|');
                                string[] steamId = playerInfo[2].Split('|');
                                string[] time = playerInfo[3].Split('|');
                                string playerName = playerInfo[4];

                                AddPlayerToGrid(slot[0], playerName, steamId[0], "Disconnected", time[0]);
                            }
                        }
                    }
                }

            }
            catch(Exception ex)
            { }

        }

        #endregion

        #region Invoke Callbacks

        private void AddPlayerToGrid(string serverSlot, string playerName, string steamId, string connectStatus, string disconnectTime)
        {
            if (grdPlayers.InvokeRequired)
            {
                AddGridRow i = new AddGridRow(AddPlayerToGrid);
                this.Invoke(i, new object[] { serverSlot, playerName, steamId, connectStatus, disconnectTime });
            }
            else
            {
                grdPlayers.Rows.Add(serverSlot, playerName, steamId, connectStatus, disconnectTime);
            }
        }

        private void AddCommandToHistoryList(string commandText)
        {
            if (lstHistory.InvokeRequired)
            {
                AddListboxItem c = new AddListboxItem(AddCommandToHistoryList);
                this.Invoke(c, new object[] { commandText });
            }
            else
            {
                lstHistory.Items.Insert(0, commandText);
            }
        }

        private void AddServerResponseText(TextBox control, string response)
        {
            if (txtResponse.InvokeRequired)
            {
                AddTextToTextbox c = new AddTextToTextbox(AddServerResponseText);
                this.Invoke(c, new object[] { control, response });
            }
            else
            {
                control.Text += response + Environment.NewLine;
            }
        }

        private void ClearCommandText(TextBox control, string response)
        {
            if (txtResponse.InvokeRequired)
            {
                AddTextToTextbox c = new AddTextToTextbox(ClearCommandText);
                this.Invoke(c, new object[] { control, response });
            }
            else
            {
                control.Text = string.Empty;
            }
        }

        private void ClearGridRows(DataGridView gridControl)
        {
            if (gridControl.InvokeRequired)
            {
                ClearGrid c = new ClearGrid(ClearGridRows);
                this.Invoke(c, new object[] { gridControl });
            }
            else
            {
                gridControl.Rows.Clear();
            }
        }

        private void ClearCommandHistory(ListBox listboxControl)
        {
            if (listboxControl.InvokeRequired)
            {
                ClearListBox c = new ClearListBox(ClearCommandHistory);
                this.Invoke(c, new object[] { listboxControl });
            }
            else
            {
                listboxControl.Items.Clear();
            }
        }


        private void SetControlEnabledState(Control control, bool isEnabled)
        {
            if (control.InvokeRequired)
            {
                SetControlEnabled c = new SetControlEnabled(SetControlEnabledState);
                this.Invoke(c, new object[] { control, isEnabled });
            }
            else
            {
                control.Enabled = isEnabled;
            }
        }
        #endregion

    }
}
