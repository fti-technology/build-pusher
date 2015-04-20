# build-pusher
Build pushing service

This Windows services will query a TFS build server for some specified builds and package up the corresponding binaries produced from the 
builds.  Those packages can be distributed to various location via FTP, Http, or Windows file copy.

See the options file for configuration information.
