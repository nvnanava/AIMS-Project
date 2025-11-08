using AIMS.Controllers.Mvc;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AIMS.Tests.Integration.Controllers
{
    public class SearchControllerTests
    {
        [Fact]
        public void Index_NullQuery_SetsViewBagToNull()
        {
            var controller = new SearchController();

            var result = controller.Index(null);

            var view = Assert.IsType<ViewResult>(result);
            Assert.Null(controller.ViewBag.SearchQuery);
        }

        [Fact]
        public void Index_EmptyQuery_SetsViewBagToNull()
        {
            var controller = new SearchController();

            var result = controller.Index("");

            var view = Assert.IsType<ViewResult>(result);
            Assert.Null(controller.ViewBag.SearchQuery);
        }

        [Fact]
        public void Index_WhitespaceQuery_SetsViewBagToNull()
        {
            var controller = new SearchController();

            var result = controller.Index("     ");

            var view = Assert.IsType<ViewResult>(result);
            Assert.Null(controller.ViewBag.SearchQuery);
        }

        [Fact]
        public void Index_NormalQuery_TrimsAndSetsViewBag()
        {
            var controller = new SearchController();

            var result = controller.Index("abc");

            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("abc", controller.ViewBag.SearchQuery);
        }

        [Fact]
        public void Index_QueryWithSpaces_TrimsAndSetsViewBag()
        {
            var controller = new SearchController();

            var result = controller.Index("   hello world   ");

            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("hello world", controller.ViewBag.SearchQuery);
        }
    }
}
