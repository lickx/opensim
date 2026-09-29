-- Create the tables used by OpenSimSearch

-- To be sourced once into your robust database:
-- change database robust;
-- source /path/to/create_search_tables.sql

-- You will also need to setup PHP helper scripts on your webserver,
-- and update OpenSim.ini to use those helpers; see [Search] section.

DROP TABLE IF EXISTS `search_allparcels`;
CREATE TABLE `search_allparcels` (
  `regionUUID` char(36) NOT NULL,
  `parcelname` varchar(255) NOT NULL,
  `ownerUUID` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `groupUUID` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `landingpoint` varchar(255) NOT NULL,
  `parcelUUID` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `infoUUID` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `parcelarea` int(11) NOT NULL,
  PRIMARY KEY (`parcelUUID`),
  KEY `regionUUID` (`regionUUID`)
);

DROP TABLE IF EXISTS `search_events`;
CREATE TABLE `search_events` (
  `owneruuid` char(36) NOT NULL,
  `name` varchar(255) NOT NULL,
  `eventid` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `creatoruuid` char(36) NOT NULL,
  `category` int(2) NOT NULL,
  `description` text NOT NULL,
  `dateUTC` int(10) NOT NULL,
  `duration` int(10) NOT NULL,
  `covercharge` tinyint(1) NOT NULL,
  `coveramount` int(10) NOT NULL,
  `simname` varchar(255) NOT NULL,
  `parcelUUID` char(36) NOT NULL,
  `globalPos` varchar(255) NOT NULL,
  `eventflags` int(1) NOT NULL,
  PRIMARY KEY (`eventid`)
);

DROP TABLE IF EXISTS `search_hostsregister`;
CREATE TABLE `search_hostsregister` (
  `host` varchar(255) NOT NULL,
  `port` int(5) NOT NULL,
  `register` int(10) NOT NULL,
  `nextcheck` int(10) NOT NULL,
  `checked` tinyint(1) NOT NULL,
  `failcounter` int(10) NOT NULL,
  PRIMARY KEY (`host`,`port`)
);

DROP TABLE IF EXISTS `search_objects`;
CREATE TABLE `search_objects` (
  `objectuuid` char(36) NOT NULL,
  `parceluuid` char(36) NOT NULL,
  `location` varchar(255) NOT NULL,
  `name` varchar(255) NOT NULL,
  `description` varchar(255) NOT NULL,
  `regionuuid` char(36) NOT NULL DEFAULT '',
  PRIMARY KEY (`objectuuid`,`parceluuid`)
);

DROP TABLE IF EXISTS `search_parcels`;
CREATE TABLE `search_parcels` (
  `regionUUID` char(36) NOT NULL,
  `parcelname` varchar(255) NOT NULL,
  `parcelUUID` char(36) NOT NULL,
  `landingpoint` varchar(255) NOT NULL,
  `description` varchar(255) NOT NULL,
  `searchcategory` varchar(50) NOT NULL,
  `build` enum('true','false') NOT NULL,
  `script` enum('true','false') NOT NULL,
  `public` enum('true','false') NOT NULL,
  `dwell` float NOT NULL DEFAULT 0,
  `infouuid` varchar(36) NOT NULL DEFAULT '',
  `mature` varchar(10) NOT NULL DEFAULT 'PG',
  `pictureUUID` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  PRIMARY KEY (`regionUUID`,`parcelUUID`),
  KEY `name` (`parcelname`),
  KEY `description` (`description`),
  KEY `searchcategory` (`searchcategory`),
  KEY `dwell` (`dwell`)
);

DROP TABLE IF EXISTS `search_parcelsales`;
CREATE TABLE `search_parcelsales` (
  `regionUUID` char(36) NOT NULL,
  `parcelname` varchar(255) NOT NULL,
  `parcelUUID` char(36) NOT NULL,
  `area` int(6) NOT NULL,
  `saleprice` int(11) NOT NULL,
  `landingpoint` varchar(255) NOT NULL,
  `infoUUID` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `dwell` int(11) NOT NULL,
  `parentestate` int(11) NOT NULL DEFAULT 1,
  `mature` varchar(10) NOT NULL DEFAULT 'PG',
  PRIMARY KEY (`regionUUID`,`parcelUUID`)
);

DROP TABLE IF EXISTS `search_popularplaces`;
CREATE TABLE `search_popularplaces` (
  `parcelUUID` char(36) NOT NULL,
  `name` varchar(255) NOT NULL,
  `dwell` float NOT NULL,
  `infoUUID` char(36) NOT NULL,
  `has_picture` tinyint(1) NOT NULL,
  `mature` varchar(10) NOT NULL,
  PRIMARY KEY (`parcelUUID`)
);

DROP TABLE IF EXISTS `search_regions`;
CREATE TABLE `search_regions` (
  `regionname` varchar(255) NOT NULL,
  `regionUUID` char(36) NOT NULL,
  `regionhandle` varchar(255) NOT NULL,
  `url` varchar(255) NOT NULL,
  `owner` varchar(255) NOT NULL,
  `owneruuid` char(36) NOT NULL,
  PRIMARY KEY (`regionUUID`)
);

