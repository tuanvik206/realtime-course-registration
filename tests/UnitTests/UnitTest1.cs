using Google.Protobuf;

namespace UnitTests;

public class UnitTest1
{
    [Fact]
    public void AdminSubjectListRequestRoundTripsIncludeInactiveFlag()
    {
        var request = new RealtimeCourseRegistration.Contracts.Capacity.V1.ListSubjectsRequest
        {
            Search = "database", Page = 2, PageSize = 50, IncludeInactive = true
        };
        var actual = RealtimeCourseRegistration.Contracts.Capacity.V1.ListSubjectsRequest.Parser.ParseFrom(request.ToByteArray());
        Assert.Equal("database", actual.Search);
        Assert.Equal(2, actual.Page);
        Assert.Equal(50, actual.PageSize);
        Assert.True(actual.IncludeInactive);
    }
}
