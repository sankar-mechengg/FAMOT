using System;
using System.Collections.Generic;
using System.IO;
using FAMOT.Session;
using NUnit.Framework;

namespace FAMOT.Tests
{
    /// <summary>Edit-mode tests for <see cref="SubjectRegistry"/> and <see cref="DataPaths"/>, using a temp data root.</summary>
    public class SubjectRegistryTests
    {
        private string _root;

        /// <summary>Redirects the data root to a fresh temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "FAMOT_RegistryTests_" + Guid.NewGuid().ToString("N"));
            DataPaths.OverrideRootDirectory(_root);
        }

        /// <summary>Restores the data root and deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            DataPaths.ClearOverride();
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup of the temp folder.
            }
        }

        private static SubjectInfo MakeSubject(string id, string name)
        {
            SubjectInfo s = SubjectInfo.Create(id, name);
            s.handedness = "Left";
            s.age = 54;
            s.sex = "Female";
            s.affectedSide = "Right";
            s.notes = "post-stroke, \"mild\"";
            s.jointLimits.maxAngles[3] = 120f;
            return s;
        }

        /// <summary>The override is honoured and the index lives directly in the root.</summary>
        [Test]
        public void Override_RedirectsRoot()
        {
            Assert.IsTrue(DataPaths.IsOverridden);
            StringAssert.StartsWith(Path.GetFullPath(_root).Replace('\\', '/'), DataPaths.RootDirectory);
            StringAssert.EndsWith("/subjects.csv", DataPaths.SubjectsIndexPath);
        }

        /// <summary>A saved subject is found again with all fields intact.</summary>
        [Test]
        public void SaveLoadFind_RoundTrip()
        {
            SubjectInfo s = MakeSubject("AB2C", "Doe, Jane");
            SubjectRegistry.Save(s);

            Assert.IsTrue(File.Exists(DataPaths.SubjectJsonPath(s)));
            Assert.IsTrue(File.Exists(DataPaths.SubjectsIndexPath));

            List<SubjectInfo> all = SubjectRegistry.LoadAll();
            Assert.AreEqual(1, all.Count);

            SubjectInfo f = SubjectRegistry.Find("ab2c");
            Assert.IsNotNull(f);
            Assert.AreEqual("AB2C", f.subjectId);
            Assert.AreEqual("Doe, Jane", f.subjectName);
            Assert.AreEqual("Left", f.handedness);
            Assert.AreEqual(54, f.age);
            Assert.AreEqual("Female", f.sex);
            Assert.AreEqual("Right", f.affectedSide);
            Assert.AreEqual("post-stroke, \"mild\"", f.notes);
            Assert.AreEqual(s.createdUtc, f.createdUtc);
            Assert.AreEqual(120f, f.jointLimits.maxAngles[3]);
            Assert.AreEqual(FAMOT.Core.ArmSide.Left, f.HandednessSide());

            Assert.IsTrue(SubjectRegistry.Exists("AB2C"));
            Assert.IsFalse(SubjectRegistry.Exists("ZZZZ"));
            Assert.IsNull(SubjectRegistry.Find("ZZZZ"));
        }

        /// <summary>The index row for a quoted name (with a comma) parses back into the same fields.</summary>
        [Test]
        public void Index_QuotedFields_ParseBack()
        {
            SubjectRegistry.Save(MakeSubject("AB2C", "Doe, Jane"));

            List<string[]> rows = SubjectRegistry.ParseCsv(File.ReadAllText(DataPaths.SubjectsIndexPath));
            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(SubjectRegistry.IndexHeader, rows[0]);
            Assert.AreEqual(6, rows[1].Length);
            Assert.AreEqual("AB2C", rows[1][0]);
            Assert.AreEqual("Doe, Jane", rows[1][1]);
        }

        /// <summary>Saving an existing subject updates its row instead of appending a duplicate.</summary>
        [Test]
        public void Save_Twice_RewritesIndexRow()
        {
            SubjectInfo a = MakeSubject("K7QM", "Alpha");
            SubjectInfo b = MakeSubject("P3XR", "Beta");
            SubjectRegistry.Save(a);
            SubjectRegistry.Save(b);

            a.sessionCount = 5;
            a.lastSessionUtc = "2026-01-02T03:04:05.0000000Z";
            SubjectRegistry.Save(a);

            List<string[]> rows = SubjectRegistry.ParseCsv(File.ReadAllText(DataPaths.SubjectsIndexPath));
            Assert.AreEqual(3, rows.Count, "header + 2 subjects");

            int matches = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i][0] == "K7QM")
                {
                    matches++;
                    Assert.AreEqual("5", rows[i][5]);
                    Assert.AreEqual("2026-01-02T03:04:05.0000000Z", rows[i][4]);
                }
            }
            Assert.AreEqual(1, matches);
            Assert.IsFalse(File.Exists(DataPaths.SubjectsIndexPath + ".tmp"), "temp file must be gone");

            Assert.AreEqual(5, SubjectRegistry.Find("K7QM").sessionCount);
            Assert.AreEqual(2, SubjectRegistry.LoadAll().Count);
        }

        /// <summary>Corrupt rows are skipped and subject folders missing from the index are discovered.</summary>
        [Test]
        public void LoadAll_ToleratesCorruptRows_AndFindsUnindexedFolders()
        {
            SubjectRegistry.Save(MakeSubject("K7QM", "Alpha"));
            File.AppendAllText(DataPaths.SubjectsIndexPath, ",,,\n\"unterminated\n\n");
            Directory.CreateDirectory(Path.Combine(DataPaths.RootDirectory, "W9TT_Orphan_Subject"));
            Directory.CreateDirectory(Path.Combine(DataPaths.RootDirectory, "not_a_subject"));

            List<SubjectInfo> all = SubjectRegistry.LoadAll();

            var ids = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                ids.Add(all[i].subjectId);
            }
            CollectionAssert.Contains(ids, "K7QM");
            CollectionAssert.Contains(ids, "W9TT");
            CollectionAssert.DoesNotContain(ids, "not_a_subject");
            Assert.AreEqual("Orphan Subject", SubjectRegistry.Find("W9TT").subjectName);
        }

        /// <summary>Generated ids are 4 characters from the unambiguous alphabet and avoid existing ids.</summary>
        [Test]
        public void GenerateUniqueId_IsUniqueAndUnambiguous()
        {
            var existing = new HashSet<string>();
            for (int n = 0; n < 500; n++)
            {
                string id = SubjectRegistry.GenerateUniqueId(existing);
                Assert.AreEqual(4, id.Length);
                foreach (char c in id)
                {
                    Assert.IsTrue(SubjectRegistry.IdAlphabet.IndexOf(c) >= 0, "unexpected char " + c);
                    Assert.IsFalse(c == '0' || c == 'O' || c == '1' || c == 'I', "ambiguous char " + c);
                }
                Assert.IsTrue(existing.Add(id), "duplicate id " + id);
            }

            Assert.AreEqual(4, SubjectRegistry.GenerateUniqueId(null).Length);
        }

        /// <summary>Session folders are created inside the subject folder and never collide.</summary>
        [Test]
        public void CreateSessionDirectory_IsUnique()
        {
            SubjectInfo s = MakeSubject("K7QM", "Alpha Beta");
            SubjectRegistry.Save(s);
            StringAssert.EndsWith("/K7QM_Alpha_Beta", DataPaths.SubjectDirectory(s));

            string d1 = DataPaths.CreateSessionDirectory(s, "VR", "L", out string id1);
            string d2 = DataPaths.CreateSessionDirectory(s, "VR", "L", out string id2);

            Assert.IsTrue(Directory.Exists(d1));
            Assert.IsTrue(Directory.Exists(d2));
            Assert.AreNotEqual(id1, id2);
            StringAssert.EndsWith("_VR_L", id1);
            StringAssert.StartsWith(DataPaths.SubjectDirectory(s), d1);
        }

        /// <summary>Sanitize replaces invalid characters and whitespace, limits length, and falls back to "Subject".</summary>
        [Test]
        public void Sanitize_Works()
        {
            Assert.AreEqual("John_Doe", DataPaths.Sanitize("  John   Doe "));
            Assert.AreEqual("a_b_c", DataPaths.Sanitize("a/b:c"));
            Assert.AreEqual("Subject", DataPaths.Sanitize(""));
            Assert.AreEqual("Subject", DataPaths.Sanitize(null));
            Assert.AreEqual("Subject", DataPaths.Sanitize("???"));
            Assert.LessOrEqual(DataPaths.Sanitize(new string('x', 100)).Length, 40);
        }
    }
}
