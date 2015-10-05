import logging, requests, sys, os, zipfile, gzip, time
from requests.auth import HTTPBasicAuth
from datetime import datetime

logger = logging.getLogger(__name__)

def initialize_logger():
    logger = logging.getLogger()
    logger.setLevel(logging.DEBUG)

    # create console handler and set level to info
    handler = logging.StreamHandler()
    formatter = logging.Formatter('%(asctime)s [%(levelname)s] - %(message)s')
    handler.setFormatter(formatter)
    logger.addHandler(handler)

    logdir = os.path.dirname(__file__) + '/logs'
    if not os.path.exists(logdir):
        os.makedirs(logdir)

    # create error file handler and set level to error
    handler = logging.FileHandler(os.path.join(logdir + '/' + os.path.basename(__file__) +  datetime.now().strftime('%Y%m%d%H%M%S') + '.log'), 'w', encoding=None, delay='true')
    formatter = logging.Formatter('%(asctime)s [%(levelname)s] - %(message)s')
    handler.setFormatter(formatter)
    logger.addHandler(handler)

def main(clusterUrl, clusterUsername, clusterPassword, topologyName, downloadOlderLogs):
    basicAuth = HTTPBasicAuth(clusterUsername, clusterPassword)
    start_time = time.time()
    logger.info('Getting topology summary from cluster: ' + clusterUrl + ' for topology: ' + topologyName)
    topologiesRequest = requests.get(clusterUrl + '/' + 'stormui/api/v1/topology/summary', auth=basicAuth)
    topologiesResponse = topologiesRequest.json()
    logger.info(topologiesResponse)

    topologies = topologiesResponse['topologies']

    topologyFilter = [i for i, xs in enumerate(topologies) if (topologies[i]['name'] == topologyName)]

    if len(topologyFilter) == 0:
        logger.error('Topology not found. Name: ' + topologyName)
        logger.info('Currently running topologies: ')
        for topology in topologies:
            logger.info(topology['name'])
        sys.exit(1)
    else:
        topologyId = topologies[topologyFilter[0]]['id']
        logger.info('Topology found with id: ' + topologyId)

    topologyUrl = clusterUrl + '/' + 'stormui/api/v1/topology/' + topologyId

    topologyRequest = requests.get(topologyUrl, auth=basicAuth)
    topologySummary = topologyRequest.json()
    logger.info('Topology summary: ' + str(topologySummary))

    components = list()

    for spout in topologySummary['spouts']:
        components.append(spout['spoutId'])

    for bolt in topologySummary['bolts']:
        components.append(bolt['boltId'])

    logger.info('Topology: ' + topologyId + ' - Component list: ' + str(components))

    logLinks = dict()
    for component in components:
        componentSummary = get_topology_component(topologyUrl, basicAuth, component)
        for executor in componentSummary['executorStats']:
            workerLogLink = clusterUrl + executor['workerLogLink'].replace('log?file=', 'download/')
            if not logLinks.has_key(workerLogLink):
                logLinks[workerLogLink] = executor['host']
            supervisorLogLink = workerLogLink[:workerLogLink.rfind('/')] + '/supervisor.log'
            if not logLinks.has_key(supervisorLogLink):
                logLinks[supervisorLogLink] = executor['host']
    logger.info('Log links: ' + str(logLinks))

    if(not downloadOlderLogs):
        logger.info('DownloadOlderLogs set to False will skip downloading older logs')
    for logLink in logLinks.iterkeys():
        logger.info(logLink)
        logFile = download_file(logLink, basicAuth, os.path.join(topologyId, logLinks[logLink]))
        # Do a best effort to download older logs
        if(downloadOlderLogs):
            try_download_olderLogs(logLink, basicAuth, os.path.join(topologyId, logLinks[logLink]))

    logs_time = time.time() - start_time
    logger.info('Time taken to download logs: %s secs' % str(logs_time))

    zip(topologyId, topologyId)
    zip_time = time.time() - start_time - logs_time
    logger.info('Time taken to zip logs: %s secs' % str(zip_time))

    total_time = time.time() - start_time
    logger.info('Total time taken: %s secs' % str(total_time))

    sys.exit(0)

def get_topology_component(topologyUrl, basicAuth, component):
    componentRequest = requests.get(topologyUrl + '/component/' + component, auth=basicAuth)
    componentSummary = componentRequest.json()
    logger.info('Component \'' + component + '\' summary: ' + str(componentSummary))
    return componentSummary

def try_download_olderLogs(url, basicAuth, dir):
    logger.info('Attempting to download older logs using ' + url + ' to ' + dir)
    # With current logback configuration we expect 10 copies with file format 'xyz.log.1'
    for num in range(1,10):
        logUrl = url + '.' + str(num)
        logger.info(logUrl)
        try:
            logFile = download_file(logUrl, basicAuth, dir)
        except Exception, e:
            logger.error('Unable to download more logs: ' + str(e))
            return

def download_file(url, basicAuth, dir):
    logger.info('Downloading ' + url + ' to ' + dir)

    if not os.path.exists(dir):
        os.makedirs(dir)

    local_filename = os.path.join(dir, url.split('/')[-1])
    # NOTE the stream=True parameter
    r = requests.get(url, auth=basicAuth, stream=True)

    with open(local_filename, 'wb') as f:
        for chunk in r.iter_content(chunk_size=4096):
            if chunk: # filter out keep-alive new chunks
                f.write(chunk)
                f.flush()
    logger.info('Download complete - ' + local_filename)
    return local_filename

def zip(src, dst):
    dstfile = "%s.zip" % (dst)
    logger.info('Creating a zip archive of ' + src)
    zf = zipfile.ZipFile(dstfile, "w", zipfile.ZIP_DEFLATED)
    for dirname, subdirs, files in os.walk(src):
        zf.write(dirname)
        for filename in files:
            zf.write(os.path.join(dirname, filename))
    zf.close()
    logger.info('Zip archive created successfully at: ' + dstfile)

if __name__ == '__main__':
    initialize_logger()
    if(len(sys.argv) < 5):
        logger.error('Missing parameters. Syntax: StormLogsDownloader.py "<CLUSTER_URL>" "<CLUSTER_USER>" "<CLUSTER_PASSWORD>" "<TOPOLOGY_NAME>" [OPTIONAL]<DOWNLOAD_OLDER_LOGS>')
        sys.exit(-1)
    downloadOlderLogs = True
    if (len(sys.argv) >= 6 and str(sys.argv[5]).upper() == 'FALSE'):
        downloadOlderLogs = False
    main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], downloadOlderLogs)
