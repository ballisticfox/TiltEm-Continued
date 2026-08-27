using System.Collections.Generic;
using Xunit;

namespace TiltEm.Verification
{
    /// <summary>Frame construction.</summary>
    public class FrameCheckTests
    {
        private const string Group = "FrameChecks";

        private static void Body() => FrameChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Threshold crossing and physics axes.</summary>
    public class TransitionCheckTests
    {
        private const string Group = "TransitionChecks";

        private static void Body() => TransitionChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>The body editor's conversions and its exported configs.</summary>
    public class EditorCheckTests
    {
        private const string Group = "EditorChecks";

        private static void Body() => EditorChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>What the editor does to the sky while it moves a body.</summary>
    public class EditorFrameCheckTests
    {
        private const string Group = "EditorFrameChecks";

        private static void Body() => EditorFrameChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Whole system, many tilted bodies at once.</summary>
    public class SystemCheckTests
    {
        private const string Group = "SystemChecks";

        private static void Body() => SystemChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Initialisation and lifecycle.</summary>
    public class LifecycleCheckTests
    {
        private const string Group = "LifecycleChecks";

        private static void Body() => LifecycleChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Parent-relative orbit frame.</summary>
    public class OrbitFrameCheckTests
    {
        private const string Group = "OrbitFrameChecks";

        private static void Body() => OrbitFrameChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Camera framing.</summary>
    public class CameraCheckTests
    {
        private const string Group = "CameraChecks";

        private static void Body() => CameraChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Parent-relative element readouts.</summary>
    public class DisplayCheckTests
    {
        private const string Group = "DisplayChecks";

        private static void Body() => DisplayChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Debug-menu teleports.</summary>
    public class TeleportCheckTests
    {
        private const string Group = "TeleportChecks";

        private static void Body() => TeleportChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Sphere-of-influence handovers.</summary>
    public class DominantBodyCheckTests
    {
        private const string Group = "DominantBodyChecks";

        private static void Body() => DominantBodyChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Save/load round trips.</summary>
    public class PersistenceCheckTests
    {
        private const string Group = "PersistenceChecks";

        private static void Body() => PersistenceChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>Shipped sources.</summary>
    public class SourceCheckTests
    {
        private const string Group = "SourceChecks";

        private static void Body() => SourceChecks.Run(SourceChecks.FindRepoRoot());

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }

    /// <summary>The worked examples from the maths document.</summary>
    //No assertions of their own - they print derivations. Run so a change that breaks one
    //shows up here rather than in the document.
    public class WorkedExampleTests
    {
        [Fact]
        public void RunsWithoutThrowing()
        {
            WorkedExamples.Run();
        }
    }
}
