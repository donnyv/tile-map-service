﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using TileMapService.Utils;

namespace TileMapService.Controllers
{
    /// <summary>
    /// TMS endpoint - serving tiles using Tile Map Service protocol (<see href="https://wiki.osgeo.org/wiki/Tile_Map_Service_Specification">Tile Map Service Specification</see>).
    /// </summary>
    [Route("tms")]
    public class TmsController : Controller
    {
        private readonly ITileSourceFabric tileSourceFabric;

        public TmsController(ITileSourceFabric tileSourceFabric)
        {
            this.tileSourceFabric = tileSourceFabric;
        }

        [HttpGet("")]
        public IActionResult GetRootResource()
        {
            // TODO: services/root.xml
            var capabilities = this.GetCapabilities();
            var xmlDoc = new Tms.CapabilitiesUtility(capabilities).GetRootResource();

            return File(xmlDoc.ToUTF8ByteArray(), MediaTypeNames.Text.Xml);
        }

        [HttpGet("1.0.0")]
        public IActionResult GetTileMapService()
        {
            // TODO: services/tilemapservice.xml
            var capabilities = this.GetCapabilities();
            var xmlDoc = new Tms.CapabilitiesUtility(capabilities).GetTileMapService();

            return File(xmlDoc.ToUTF8ByteArray(), MediaTypeNames.Text.Xml);
        }

        [HttpGet("1.0.0/{tileset}")]
        public IActionResult GetTileMap(string tileset)
        {
            // TODO: services/basemap.xml
            var capabilities = this.GetCapabilities();
            var layer = capabilities.Layers?.SingleOrDefault(l => l.Identifier == tileset);
            if (layer == null)
            {
                return NotFound(); // TODO: errors in XML format
            }

            var xmlDoc = new Tms.CapabilitiesUtility(capabilities).GetTileMap(layer);

            return File(xmlDoc.ToUTF8ByteArray(), MediaTypeNames.Text.Xml);
        }

        /// <summary>
        /// Get tile from tileset with specified coordinates.
        /// </summary>
        /// <param name="tileset">Tileset (source) name.</param>
        /// <param name="x">Tile X coordinate (column).</param>
        /// <param name="y">Tile Y coordinate (row), Y axis goes up from the bottom.</param>
        /// <param name="z">Tile Z coordinate (zoom level).</param>
        /// <param name="extension">File extension.</param>
        /// <returns>Response with tile contents.</returns>
        [HttpGet("1.0.0/{tileset}/{z}/{x}/{y}.{extension}")]
        public async Task<IActionResult> GetTileAsync(string tileset, int x, int y, int z, string extension)
        {
            // TODO: z can be a string, not integer number
            if (String.IsNullOrEmpty(tileset) || String.IsNullOrEmpty(extension))
            {
                return BadRequest();
            }

            if (this.tileSourceFabric.Contains(tileset))
            {
                // TODO: ? convert source format to requested output format
                var tileSource = this.tileSourceFabric.Get(tileset);

                if (IsOutOfBBox(x, y, z, tileSource.Configuration.Srs))
                {
                    return ResponseWithNotFoundError("The requested tile is outside the bounding box of the tile map.");
                }

                var data = await tileSource.GetTileAsync(x, y, z);
                if (data != null)
                {
                    return File(data, tileSource.Configuration.ContentType);
                }
                else
                {
                    return NotFound();
                }
            }
            else
            {
                return NotFound($"Specified tileset '{tileset}' not found");
            }
        }

        private IActionResult ResponseWithNotFoundError(string message)
        {
            var xmlDoc = new Tms.TileMapServerError(message).ToXml();
            Response.ContentType = MediaTypeNames.Text.Xml + "; charset=utf-8"; // TODO: better way?
            Response.StatusCode = (int)HttpStatusCode.NotFound;
            return File(xmlDoc.ToUTF8ByteArray(), Response.ContentType);
        }

        private static bool IsOutOfBBox(int x, int y, int z, string? srs) // TODO: shared function
        {
            int xmin, xmax;
            int ymin = 0;
            int ymax = z << 1;
            switch (srs)
            {
                case SrsCodes.EPSG3857: { xmin = 0; xmax = 1 << z; break; }
                case SrsCodes.EPSG4326: { xmin = 0; xmax = 2 * (1 << z); break; }
                default: throw new ArgumentOutOfRangeException(nameof(srs));
            }

            return x < xmin || x > xmax || y < ymin || y > ymax;
        }

        private Tms.Capabilities GetCapabilities()
        {
            var layers = EntitiesConverter.SourcesToLayers(this.tileSourceFabric.Sources);
            return new Tms.Capabilities
            {
                BaseUrl = this.BaseUrl,
                Layers = layers.ToArray(),
            };
        }

        private string BaseUrl
        {
            get
            {
                return $"{this.Request.Scheme}://{this.Request.Host}{this.Request.PathBase}";
            }
        }
    }
}
