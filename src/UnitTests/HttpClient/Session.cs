﻿using DuetAPI.ObjectModel;
using DuetHttpClient;
using DuetHttpClient.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace UnitTests.HttpClient
{
    [TestFixture]
    public class Session
    {
        private DuetHttpSession session;

        [OneTimeSetUp]
        public async Task Connect()
        {
            // This has to be changed depending on the test setup.
            // The IP address must be either a Duet (standalone mode) or a SBC running DSF (SBC mode)
            session = await DuetHttpSession.ConnectAsync(new("http://ender3pro"));
        }

        [Test]
        public async Task ObjectModel()
        {
            // Wait a moment for the object model to be up-to-date
            for (int i = 0; i < 40; i++)
            {
                lock (session.Model)
                {
                    if (session.Model.State.Status != MachineStatus.Starting)
                    {
                        // Machine model has been updated
                        break;
                    }
                }
                await Task.Delay(250);
            }
            Assert.AreNotEqual(MachineStatus.Starting, session.Model.State.Status);

            // Save the current uptime
            int now;
            lock (session.Model)
            {
                now = session.Model.State.UpTime;
            }

            // Wait another moment and for UpTime to change
            for (int i = 0; i < 40; i++)
            {
                lock (session.Model)
                {
                    if (session.Model.State.UpTime > now)
                    {
                        // Machine model has been updated
                        break;
                    }
                }
                await Task.Delay(250);
            }

            // Make sure the object model is updated
            lock (session.Model)
            {
                Assert.Greater(session.Model.State.UpTime, now);
            }
        }

        [Test]
        public async Task Codes()
        {
            // Make sure there are no timeouts
            await session.SendCode("G4 S6");

            // Check generic G-code reply
            string response = await session.SendCode("M115");
            Assert.IsTrue(response.StartsWith("FIRMWARE"));
        }

        [Test]
        public async Task Files()
        {
            string uploadContent = Guid.NewGuid().ToString();

            // Upload a test file
            using (MemoryStream uploadStream = new())
            {
                uploadStream.Write(Encoding.UTF8.GetBytes(uploadContent));
                uploadStream.Seek(0, SeekOrigin.Begin);

                await session.Upload("0:/sys/unitTest.txt", uploadStream);
            }

            // Download it again
            using (HttpResponseMessage downloadResponse = await session.Download("0:/sys/unitTest.txt"))
            {
                string downloadContent = await downloadResponse.Content.ReadAsStringAsync();
                Assert.AreEqual(uploadContent, downloadContent);
            }

            // Move it
            await session.Move("0:/sys/unitTest.txt", "0:/sys/unitTest2.txt", true);

            // Delete it again
            await session.Delete("0:/sys/unitTest2.txt");
        }

        [Test]
        public async Task Directories()
        {
            // Create a new directory
            await session.MakeDirectory("0:/sys/unitTest");

            // Delete it again
            await session.Delete("0:/sys/unitTest");
        }

        [Test]
        public async Task FileList()
        {
            // List files in 0:/sys and check for valid config.g
            IEnumerable<FileListItem> fileList = await session.GetFileList("0:/sys");
            Assert.IsTrue(fileList.Any(item => !item.IsDirectory && item.Filename == "config.g" && item.Size > 0 && item.Size < 192_000));

            // List root directories and check for sys directory
            fileList = await session.GetFileList("0:/");
            Assert.IsTrue(fileList.Any(item => item.IsDirectory && item.Filename == "sys"));
        }

        [Test]
        public async Task FileInfo()
        {
            // Get fileinfo for 0:/sys/config.g
            ParsedFileInfo info = await session.GetFileInfo("0:/sys/config.g");
            Assert.Greater(info.Size, 0);
            Assert.Less(info.Size, 192_000);
        }

        [OneTimeTearDown]
        public async Task Disconnect()
        {
            await session.DisposeAsync();
        }
    }
}