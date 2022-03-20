﻿using System;
using System.Threading.Tasks;

using Npgsql;

namespace TileMapService.TileSources
{
    /// <summary>
    /// Represents tile source with vector tiles (MVT) from PostGIS database.
    /// </summary>
    class PostGISTileSource : ITileSource
    {
        private SourceConfiguration configuration;

        private string connectionString;

        public PostGISTileSource(SourceConfiguration configuration)
        {
            if (String.IsNullOrEmpty(configuration.Id))
            {
                throw new ArgumentException("Source identifier is null or empty string");
            }

            if (String.IsNullOrEmpty(configuration.Location))
            {
                throw new ArgumentException("Source location is null or empty string");
            }

            this.connectionString = configuration.Location;
            this.configuration = configuration; // Will be changed later in InitAsync
        }

        #region ITileSource implementation

        Task ITileSource.InitAsync()
        {
            var title = String.IsNullOrEmpty(this.configuration.Title) ?
                    this.configuration.Id :
                    this.configuration.Title;

            var minZoom = this.configuration.MinZoom ?? 0;
            var maxZoom = this.configuration.MaxZoom ?? 20;

            // Re-create configuration
            this.configuration = new SourceConfiguration
            {
                Id = this.configuration.Id,
                Type = this.configuration.Type,
                Format = ImageFormats.MapboxVectorTile,
                Title = title,
                Tms = this.configuration.Tms ?? true,
                Srs = Utils.SrsCodes.EPSG3857,
                Location = this.configuration.Location,
                ContentType = Utils.EntitiesConverter.TileFormatToContentType(ImageFormats.MapboxVectorTile),
                MinZoom = minZoom,
                MaxZoom = maxZoom,
                GeographicalBounds = null,
                TileWidth = Utils.WebMercator.DefaultTileWidth,
                TileHeight = Utils.WebMercator.DefaultTileHeight,
                Cache = null, // TODO: ? possible to implement
                Table = configuration.Table,
            };

            return Task.CompletedTask;
        }

        async Task<byte[]?> ITileSource.GetTileAsync(int x, int y, int z)
        {
            if ((z < this.configuration.MinZoom) ||
                (z > this.configuration.MaxZoom) ||
                (x < 0) ||
                (y < 0) ||
                (x > Utils.WebMercator.TileCount(z)) ||
                (y > Utils.WebMercator.TileCount(z)))
            {
                return null;
            }
            else
            {
                var table = this.configuration.Table;
                if (table == null)
                {
                    throw new InvalidOperationException("Table must be defined.");
                }

                if (String.IsNullOrWhiteSpace(table.Name))
                {
                    throw new InvalidOperationException("Table name must be defined.");
                }

                if (String.IsNullOrWhiteSpace(table.Geometry))
                {
                    throw new InvalidOperationException("Table geometry field must be defined.");
                }

                string[]? fields = null;
                if (!String.IsNullOrWhiteSpace(table.Fields))
                {
                    fields = table.Fields.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }

                return await this.ReadPostGISVectorTileAsync(
                    table.Name,
                    table.Geometry,
                    fields,
                    x, Utils.WebMercator.FlipYCoordinate(y, z), z);
            }
        }

        SourceConfiguration ITileSource.Configuration
        {
            get
            {
                return this.configuration;
            }
        }

        #endregion

        private async Task<byte[]?> ReadPostGISVectorTileAsync(
            string tableName,
            string geometry,
            string[]? fields,
            int x, int y, int z)
        {
            // https://blog.crunchydata.com/blog/dynamic-vector-tiles-from-postgis
            // https://postgis.net/docs/manual-3.0/ST_AsMVT.html

            var commandText = $@"
                    WITH mvtgeom AS
                    (
                        SELECT ST_AsMVTGeom({geometry}, ST_TileEnvelope({z},{x},{y})) AS geom 
                            {((fields != null && fields.Length > 0) ? ", " + String.Join(',', fields) : String.Empty)}
                        FROM ""{tableName}""
                        WHERE ST_Intersects({geometry}, ST_TileEnvelope({z},{x},{y}))
                    )
                    SELECT ST_AsMVT(mvtgeom.*, '{this.configuration.Id}')
                    FROM mvtgeom";
            
            // TODO: ? other SRS using reprojection, if needed
            using var connection = new NpgsqlConnection(this.connectionString);
            await connection.OpenAsync();
            using var command = new NpgsqlCommand(commandText, connection);
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader[0] as byte[];
            }
            else
            {
                return null;
            }
        }
    }
}
