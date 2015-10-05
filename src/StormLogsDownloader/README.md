# Apache Storm Logs Downloader for Azure HDInsight
This python script allows you to download all the topology logs from a Azure HDInsight Apache Storm cluster.

The script relies on STORM UI REST APIs to retreive the worker log links and then downloads them one by one from each node for that topology.
It also downloads the supervisor logs from all those workers.

## Pre-Requisites
* [Python 2.7+](https://www.python.org/)
* [Python requests](https://pypi.python.org/pypi/requests) package - ```pip install requests```
* [OPTIONAL] [Python Tools for Visual Studio](http://microsoft.github.io/PTVS/)

## Usage
* Run the script by providing your cluster url, user name, password and the topology name for which you want to download the logs
  * You need to provide your cluster http username and password that you provided at cluster create time for the basic auth to work
```
python StormLogsDownloader.py "<CLUSTER_URL>" "<CLUSTER_USER>" "<CLUSTER_PASSWORD>" "<TOPOLOGY_NAME>" [OPTIONAL]<DOWNLOAD_OLDER_LOGS>
```
* Or you can use the provided Visual Studio solution file to run via 'F5' (You will need PTVS)
* The logs for this script are created under the ```logs``` directory which you can use to create issues or ask support for
* If the logs are too big or it is taking too long to download all older copies, you can choose to skip downloading older logs by passing ```False``` to the final 'DOWNLOAD_OLDER_LOGS' argument.

## Compatibility Notes
This package is fully compatible with Azure HDInsight Apache Storm cluster for both Windows and Linux flavors.

### Running this script on Windows 10?
Installing Python on Windows 10 may not add it to your system path.
You will need to create a system variable called as PYTHON_HOME with a path like ```c:\python27``` and then add PYTHON_HOME to your system path.

## HDInsight Storm Logs URI format
On a Windows cluster the supervisors are registered with nimbus using their host names, however on a linux cluster they currently show up as ip addresses.

* Nimbus log on Headnode - CLUSTER_URL/stormui/hn/log?file=nimbus.log
* Supervisor log on Workernode - CLUSTER_URL/stormui/wn0/log?file=supervisor.log
  * Linux - CLUSTER_URL/stormui/10.0.0.X/log?file=supervisor.log
* Worker log on Workernode - CLUSTER_URL/stormui/wn0/log?file=worker-port.log
  * Linux - CLUSTER_URL/stormui/10.0.0.X/log?file=worker-port.log

## References
* [STORM UI REST API](https://github.com/apache/storm/blob/master/STORM-UI-REST-API.md)
* [Python Tools for Visual Studio](http://microsoft.github.io/PTVS/)

## TODO (Future extensibility)
* Add Nimbus log downloading
  * An issue on Windows clusters prevents the links from working, it will be soon live in production.
  * The limitation of not having logview service running on headnode prevents to download Nimbus logs on Linux clusters.
* Add a flag to download logs of every topology
  * This is easily extensible as we have to just loop over the topologies summary to run the log downloader on topology ids.
* Parallelize log downloading to reduce time to download for a long running topology
* Add more retry-ability (use retrying package perhaps)